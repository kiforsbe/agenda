using Agenda.App.Services;
using Agenda.Core;
using Agenda.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Agenda.App;

public sealed partial class MainWindow
{
    private async Task InitializeConnectionsAsync()
    {
        try {
            _feeds=await FeedConfigurationStore.LoadAsync(App.DataDirectory);
            if (_settings.SourceMode=="feeds"&&_feeds.Count>0) await SwitchConnectionAsync(new IcsCalendarProvider(_feeds,App.DataDirectory),"feeds",false);
            else if (_settings.SourceMode=="windows") await SwitchConnectionAsync(new WindowsCalendarProvider(),"windows",true);
            else if (_settings.SourceMode=="microsoft"&&Guid.TryParse(_settings.ClientId,out var id)&&id!=Guid.Empty)
                await SwitchConnectionAsync(NewMicrosoftProvider(),"microsoft",false);
        } catch(Exception ex) { Status(FriendlyError(ex),InfoBarSeverity.Warning); }
    }

    private MicrosoftCalendarProvider NewMicrosoftProvider() => new(WinRT.Interop.WindowNative.GetWindowHandle(this),_settings.ClientId,App.DataDirectory);

    private async Task SwitchConnectionAsync(ICalendarProvider candidate,string mode,bool signIn)
    {
        if (_authBusy) { (candidate as IDisposable)?.Dispose(); return; }
        _authBusy=true; _refreshCts?.Cancel();
        await _operationGate.WaitAsync();
        var switched=false;
        try {
            if(signIn) await candidate.SignInAsync();
            // Validate access before replacing a working source. Graph startup stays silent.
            var calendars=await candidate.GetCalendarsAsync();
            var previousMode=_settings.SourceMode;
            try { _settings.SourceMode=mode;_settings.Save(App.DataDirectory); }
            catch { _settings.SourceMode=previousMode;throw; }
            var previous=_provider; _provider=candidate;switched=true;
            _events=[];_calendars=calendars;
            (previous as IDisposable)?.Dispose();
            StatusBar.IsOpen=false;SyncText.Text="Loading events…";
            SetPanel("agenda");RenderAll();
        } catch(OperationCanceledException) { Status("Connection cancelled."); }
        catch(Exception ex) { Status(FriendlyError(ex),InfoBarSeverity.Warning); }
        finally { if(!switched)(candidate as IDisposable)?.Dispose();_operationGate.Release();_authBusy=false; }
        if(switched) await RefreshAsync();
    }

    private void SetRefreshStatus(ICalendarProvider provider)
    {
        StatusBar.IsOpen=false;
        if(provider is WindowsCalendarProvider) { SyncText.Text="Windows local store";return; }
        if(provider is MicrosoftCalendarProvider microsoft && microsoft.CacheStatus.State==MicrosoftCalendarDataState.OfflineCache) {
            SyncText.Text="Offline · cached";Status($"Showing saved events from {microsoft.CacheStatus.RefreshedAt?.ToLocalTime().ToString("g",_culture)}.",InfoBarSeverity.Warning);return;
        }
        if(provider is IcsCalendarProvider feeds) {
            var stale=feeds.FeedStatuses.Values.Any(s=>s.State!=CalendarFeedDataState.Fresh);
            if(stale) { SyncText.Text="Feed update incomplete";Status("Some feeds could not refresh. Saved events may be out of date; check your calendar service.",InfoBarSeverity.Warning);return; }
            SyncText.Text=$"Feeds · {DateTime.Now.ToString("t",_culture)}";return;
        }
        SyncText.Text=$"Updated {DateTime.Now.ToString("t",_culture)}";
    }

    private async void WindowsCalendars_Click(object sender,RoutedEventArgs e) => await SwitchConnectionAsync(new WindowsCalendarProvider(),"windows",true);
    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        if(_authBusy)return;
        if(!Guid.TryParse(_settings.ClientId,out var id)||id==Guid.Empty) { SetPanel("settings");Status("Microsoft sign-in requires the app publisher’s client ID. Windows calendars and personal Google/Outlook feeds work without one.");return; }
        await SwitchConnectionAsync(NewMicrosoftProvider(),"microsoft",true);
        ShowFlyout();
    }

    private async void SignOut_Click(object sender,RoutedEventArgs e)
    {
        if(_authBusy||_provider is null)return;
        _authBusy=true;_refreshCts?.Cancel();await _operationGate.WaitAsync();
        var provider=_provider;_provider=null;_events=[];_calendars=[];
        try { await provider.SignOutAsync(); }
        catch(Exception ex) { Status(FriendlyError(ex),InfoBarSeverity.Warning); }
        finally {
            (provider as IDisposable)?.Dispose();_settings.SourceMode="none";_settings.HiddenCalendarIds.Clear();
            try { _settings.Save(App.DataDirectory); } catch(Exception ex) { Status("Could not save the disconnected state: "+ex.Message,InfoBarSeverity.Warning); }
            _operationGate.Release();_authBusy=false;SyncText.Text="Not connected";RenderAll();
        }
    }

    private void SaveClientId_Click(object sender,RoutedEventArgs e)
    {
        if(_authBusy)return;
        if(!Guid.TryParse(ClientIdBox.Text.Trim(),out var id)||id==Guid.Empty) { Status("Enter a valid application (client) ID.",InfoBarSeverity.Warning);return; }
        _settings.ClientId=id.ToString();
        try { _settings.Save(App.DataDirectory);Status("Application ID saved. Use Microsoft sign-in to connect with this registration."); }
        catch(Exception ex) { Status(ex.Message,InfoBarSeverity.Error); }
    }

    private async void Feeds_Click(object sender,RoutedEventArgs e)
    {
        if(_authBusy)return;
        try {
            _feeds=await FeedConfigurationStore.LoadAsync(App.DataDirectory);
            // Opening feed management also restores saved feeds from a legacy
            // source or disconnected profile, without a separate activation button.
            if(_feeds.Count>0 && _provider is not IcsCalendarProvider)
                await SwitchConnectionAsync(new IcsCalendarProvider(_feeds,App.DataDirectory),"feeds",false);
            RenderFeeds();SetPanel("feeds");
        }
        catch(Exception ex) { Status(FriendlyError(ex),InfoBarSeverity.Warning); }
    }

    private void RenderFeeds()
    {
        FeedList.Children.Clear();
        foreach(var feed in _feeds) {
            var row=new Grid { ColumnSpacing=8 };
            row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            row.Children.Add(CreateFeedColorButton(feed));
            var name=new TextBlock { Text=feed.Name,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center };
            Grid.SetColumn(name,1);row.Children.Add(name);
            var edit=new Button { Content=new FontIcon { Glyph="\uE70F",FontSize=14 },Width=32,Height=32,MinWidth=0,MinHeight=0,Padding=new Thickness(4) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(edit,$"Edit {feed.Name}");
            ToolTipService.SetToolTip(edit,"Edit feed");
            edit.Click+=async (_,_)=>await ShowFeedDialogAsync(feed);
            Grid.SetColumn(edit,2);row.Children.Add(edit);
            var remove=new Button { Content=new FontIcon { Glyph="\uE74D",FontSize=14 },Width=32,Height=32,MinWidth=0,MinHeight=0,Padding=new Thickness(4) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove,$"Remove {feed.Name}");
            ToolTipService.SetToolTip(remove,"Remove feed");
            Grid.SetColumn(remove,3);row.Children.Add(remove);
            remove.Click+=async (_,_)=>await RemoveFeedAsync(feed.Id);
            FeedList.Children.Add(row);
        }
    }

    private string? _editingFeedId;

    private async void AddFeed_Click(object sender,RoutedEventArgs e) => await ShowFeedDialogAsync(null);

    private async Task ShowFeedDialogAsync(FeedConfiguration? feed)
    {
        if(_authBusy || _dialogOpen)return;
        FeedErrorBar.IsOpen=false;
        _editingFeedId=feed?.Id;
        AddFeedDialog.Title=feed is null?"Add calendar feed":"Edit calendar feed";
        AddFeedDialog.PrimaryButtonText=feed is null?"Add":"Save";
        FeedNameBox.Text=feed?.Name??"";
        FeedLocationBox.Password=feed?.Location??"";
        _newFeedColor=feed?.Color??FeedColorPalette.PickNext(_feeds.Select(item=>item.Color));
        FeedColorPicker.Children.Clear();
        FeedColorPicker.Children.Add(CreateFeedPalette(_newFeedColor,color=>_newFeedColor=color));
        AddFeedDialog.XamlRoot=Root.XamlRoot;
        _dialogOpen=true;
        ContentDialogResult result;
        try { result=await AddFeedDialog.ShowAsync(); }
        finally {
            _dialogOpen=false;
            FeedNameBox.Text="";
            FeedLocationBox.Password="";
            _editingFeedId=null;
        }
        if(result==ContentDialogResult.Primary) {
            await SwitchConnectionAsync(new IcsCalendarProvider(_feeds,App.DataDirectory),"feeds",false);
            SetPanel("feeds");
        }
    }

    private async void AddFeedDialog_PrimaryButtonClick(ContentDialog sender,ContentDialogButtonClickEventArgs args)
    {
        args.Cancel=true;
        if(_authBusy)return;
        var name=FeedNameBox.Text.Trim();var location=FeedLocationBox.Password.Trim();
        if(name.Length==0||location.Length==0) {
            FeedErrorBar.Message="Enter a calendar name and an ICS link or absolute file path.";
            FeedErrorBar.IsOpen=true;
            if(name.Length==0) FeedNameBox.Focus(FocusState.Programmatic);
            else FeedLocationBox.Focus(FocusState.Programmatic);
            return;
        }
        var deferral=args.GetDeferral();
        var candidate=_editingFeedId is null
            ? _feeds.Append(new FeedConfiguration("feed-"+Guid.NewGuid().ToString("N"),name,location,_newFeedColor)).ToArray()
            : _feeds.Select(feed=>feed.Id==_editingFeedId?feed with { Name=name,Location=location,Color=_newFeedColor }:feed).ToArray();
        _authBusy=true;AddFeedButton.IsEnabled=false;
        try {
            await FeedConfigurationStore.SaveAsync(App.DataDirectory,candidate);
            _feeds=candidate;RenderFeeds();args.Cancel=false;
        }
        catch(Exception ex) { FeedErrorBar.Message=FriendlyError(ex);FeedErrorBar.IsOpen=true; }
        finally { _authBusy=false;AddFeedButton.IsEnabled=true;deferral.Complete(); }
    }

    private async Task RemoveFeedAsync(string id)
    {
        if(_authBusy)return;
        _authBusy=true;
        try { var remaining=_feeds.Where(f=>f.Id!=id).ToArray();await FeedConfigurationStore.SaveAsync(App.DataDirectory,remaining);_feeds=remaining;RenderFeeds(); }
        catch(Exception ex) { Status(FriendlyError(ex),InfoBarSeverity.Warning);return; }
        finally { _authBusy=false; }
        if(_provider is IcsCalendarProvider) await SwitchConnectionAsync(new IcsCalendarProvider(_feeds,App.DataDirectory),"feeds",false);
        SetPanel("feeds");
    }

    private async void UseFeeds_Click(object sender,RoutedEventArgs e) { if(_feeds.Count==0) { Status("Add a calendar link or file first.");return; } await SwitchConnectionAsync(new IcsCalendarProvider(_feeds,App.DataDirectory),"feeds",false); }
    private async void GoogleCalendar_Click(object sender,RoutedEventArgs e) { HideFlyout();await OpenUri("https://calendar.google.com/calendar/u/0/r"); }
    private async void OutlookCalendar_Click(object sender,RoutedEventArgs e) { HideFlyout();await OpenUri("https://outlook.live.com/calendar/0/view/month"); }
    private async void GoogleFeedHelp_Click(object sender,RoutedEventArgs e) { HideFlyout();await OpenUri("https://support.google.com/calendar/answer/37648?hl=en"); }
    private async void OutlookFeedHelp_Click(object sender,RoutedEventArgs e) { HideFlyout();await OpenUri("https://support.microsoft.com/en-us/outlook/share-your-calendar-in-outlook-com"); }
}
