using System.Globalization;
using Agenda.App.Services;
using Agenda.App.Shell;
using Agenda.Core;
using Agenda.Providers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Agenda.App;

public sealed partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private CultureInfo _culture;
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _month = DateOnly.FromDateTime(DateTime.Today);
    private IReadOnlyList<CalendarSource> _calendars = [];
    private IReadOnlyList<CalendarEvent> _events = [];
    private ICalendarProvider? _provider;
    private ShellHost? _shell;
    private IDisposable? _frame;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _trayDismissTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private CancellationTokenSource? _refreshCts;
    private readonly SemaphoreSlim _operationGate = new(1,1);
    private IReadOnlyList<FeedConfiguration> _feeds = [];
    private bool _ready, _collapsed, _visible = true, _dialogOpen, _authBusy, _saving, _exiting;
    private string _panel = "agenda";
    private DateOnly? _renderedAgendaDate;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {App.Version}";
        _culture = RegionalSettings.Read();
        _settings = AppSettings.Load(App.DataDirectory);
        Title = "Agenda";
        SystemBackdrop = new DesktopAcrylicBackdrop();
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Closing += (_, e) => { if (!_exiting) { e.Cancel = true; HideFlyout(); } };
        Activated += (_, e) => {
            if(e.WindowActivationState!=WindowActivationState.Deactivated) { _trayDismissTimer.Stop();return; }
            if(_dialogOpen || _authBusy)return;
            // Explorer activates on mouse-down; our tray callback toggles on release.
            // Preserve visibility through that gesture so release closes the open flyout.
            if(_shell?.IsTrayInteraction==true)_trayDismissTimer.Start();
            else HideFlyout();
        };
        _trayDismissTimer.Tick+=(_,_)=>{
            // Also dismiss a cancelled gesture (for example dragging away from the icon).
            if(_shell?.IsTrayInteraction!=true)HideFlyout();
        };
        Root.ActualThemeChanged += (_, _) => { RenderCalendar(); ApplyFrame(); };
        WeekToggle.IsOn = _settings.ShowWeekNumbers;
        IsoToggle.IsOn = _settings.UseIsoWeeks;
        _settings.InterceptClock = false;
        ClientIdBox.Text = _settings.ClientId;
        _clockTimer.Tick += (_, _) => {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var yesterday = DateOnly.FromDateTime(_lastClockDate);
            UpdateClock();
            if (today != yesterday) { RenderCalendar(); RenderAgenda(); }
        };
        _refreshTimer.Tick += async (_, _) => { if (_provider?.AccountName is not null && !_authBusy) await RefreshAsync(); };
        _ready = true;
        RenderAll();
    }

    public void InitializeShell()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _frame=FlyoutFrame.Attach(handle);
        ApplyFrame();
        _shell = new ShellHost(handle,
            () => DispatcherQueue.TryEnqueue(ToggleFlyout),
            () => DispatcherQueue.TryEnqueue(ExitApp),
            () => DispatcherQueue.TryEnqueue(() => { ShowFlyout(); SetPanel("settings"); }),
            () => DispatcherQueue.TryEnqueue(RegionalChanged),
            () => DispatcherQueue.TryEnqueue(HideFlyout));
        _shell.SetClockIntegration(false);
        ShowFlyout();
        _refreshTimer.Start();
        if (Environment.GetCommandLineArgs().Contains("--probe-windows")) _ = ProbeWindowsAsync();
        else _ = InitializeConnectionsAsync();
    }

    private void ApplyFrame() => FlyoutFrame.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this),Root.ActualTheme==ElementTheme.Dark);

    private async Task ProbeWindowsAsync()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try {
            var windows=new WindowsCalendarProvider(); await windows.SignInAsync(timeout.Token);
            var sources=await windows.GetCalendarsAsync(timeout.Token);
            var events=await windows.GetEventsAsync(sources,DateTimeOffset.Now.AddDays(-7),DateTimeOffset.Now.AddDays(30),timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(App.DataDirectory,"windows-probe.txt"),$"Windows store accessible: {sources.Count} calendars; {events.Count} events in a 37-day range.");
            Status($"Windows exposes {sources.Count} calendars and {events.Count} events. Choose Windows calendars in Settings to use them.");
        } catch(Exception e) {
            await File.WriteAllTextAsync(Path.Combine(App.DataDirectory,"windows-probe.txt"),$"Windows store unavailable: {e.GetType().Name}; HRESULT 0x{e.HResult:X8}; {e.Message}");
            Status("Windows did not expose a usable calendar store. You can connect personal Google and Outlook calendar feeds.",InfoBarSeverity.Warning);
        }
    }

    private DateTime _lastClockDate = DateTime.Today;
    private void UpdateClock()
    {
        _lastClockDate = DateTime.Now;
        ClockText.Text = _lastClockDate.ToString("t", _culture);
        DateText.Text = _lastClockDate.ToString("D", _culture);
    }

    private void RegionalChanged()
    {
        _culture = RegionalSettings.Read();
        RenderAll();
        Position();
        _ = RefreshAsync();
    }

    private void RenderAll() { UpdateClock(); RenderCalendar(); RenderAgenda(); RenderSettings(); }

    private void RenderCalendar()
    {
        if (!_ready) return;
        MonthText.Text = _month.ToDateTime(TimeOnly.MinValue).ToString("Y", _culture);
        MonthGrid.Children.Clear(); MonthGrid.RowDefinitions.Clear(); MonthGrid.ColumnDefinitions.Clear();
        var weekOffset = _settings.ShowWeekNumbers ? 1 : 0;
        if (weekOffset == 1) MonthGrid.ColumnDefinitions.Add(new() { Width = new GridLength(26) });
        for (var i = 0; i < 7; i++) MonthGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        MonthGrid.RowDefinitions.Add(new() { Height = new GridLength(30) });
        for (var i = 0; i < 6; i++) MonthGrid.RowDefinitions.Add(new() { Height = new GridLength(42) });
        var rows = CalendarMath.BuildMonth(_month, _culture, _settings.UseIsoWeeks);
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var today = DateOnly.FromDateTime(DateTime.Today);
        void Add(FrameworkElement element, int row, int column) { Grid.SetRow(element, row); Grid.SetColumn(element, column); MonthGrid.Children.Add(element); }
        TextBlock Label(string text) => new() { Text = text, FontSize = 11, Foreground = secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (weekOffset == 1) {
            var divider=new Border {
                Width=1,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,4,0,4),
                Background=(Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],IsHitTestVisible=false
            };
            Grid.SetRowSpan(divider,7);Add(divider,0,0);
            var label=Label("Wk"); AutomationProperties.SetName(label,"Week number"); Add(label,0,0);
        }
        for (var col = 0; col < 7; col++) Add(Label(_culture.DateTimeFormat.GetShortestDayName(rows[0].Days[col].DayOfWeek)),0,col+weekOffset);
        var activeMonthStart = CalendarMath.MonthStart(_month, _culture);
        var visibleCalendars=_calendars.Where(calendar=>IsCalendarVisible(calendar.Id)).ToArray();
        foreach (var (week, row) in rows.Select((week, row) => (week,row)))
        {
            if (weekOffset == 1) { var label=Label(week.Week.Number.ToString(_culture)); AutomationProperties.SetName(label,$"Week {week.Week.Number}, {week.Week.Year}"); Add(label,row+1,0); }
            for (var column = 0; column < 7; column++)
            {
                var date = week.Days[column];
                var dayNumber = _culture.DateTimeFormat.Calendar.GetDayOfMonth(date.ToDateTime(TimeOnly.MinValue));
                var daySources=CalendarMath.SourcesOnDay(_events,visibleCalendars,date,TimeZoneInfo.Local);
                var hasEvents=daySources.Count>0;
                var content = new Grid { Width=36,Height=36 };
                content.RowDefinitions.Add(new() { Height=new GridLength(26) });
                content.RowDefinitions.Add(new() { Height=new GridLength(10) });
                var text = new TextBlock { Text = dayNumber.ToString(_culture), HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, FontSize=14 };
                content.Children.Add(text);
                if (hasEvents) {
                    var marker=CreateDaySourceMarker(daySources);
                    Grid.SetRow(marker,1);content.Children.Add(marker);
                }
                var button = new Button { Content=content,Width=40,Height=40,MinWidth=0,MinHeight=0,Padding=new Thickness(0),Margin=new Thickness(1),CornerRadius=new CornerRadius(20),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(date==_selected?1:0),BorderBrush=accent };
                if (date==today) { button.Background=accent; text.Foreground=(Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]; }
                else if (CalendarMath.MonthStart(date,_culture)!=activeMonthStart) text.Foreground=secondary;
                AutomationProperties.SetName(button,date.ToDateTime(TimeOnly.MinValue).ToString("D",_culture) + (hasEvents?", events from "+string.Join(", ",daySources.Select(source=>source.Name)):"") + (_settings.ShowWeekNumbers?$", week {week.Week.Number}":""));
                button.Click += (_,_) => { _selected=date; SetPanel("agenda"); RenderCalendar(); RenderAgenda(); };
                button.KeyDown += (_,e) => Day_KeyDown(date,e);
                Add(button,row+1,column+weekOffset);
            }
        }
    }

    private void Day_KeyDown(DateOnly date, KeyRoutedEventArgs e)
    {
        var delta = e.Key switch { VirtualKey.Left => -1,VirtualKey.Right => 1,VirtualKey.Up => -7,VirtualKey.Down => 7,_=>0 };
        if (delta==0) return;
        e.Handled=true; _selected=date.AddDays(delta);
        if (CalendarMath.MonthStart(_selected,_culture)!=CalendarMath.MonthStart(_month,_culture)) { _month=_selected; _=RefreshAsync(); }
        RenderCalendar(); RenderAgenda();
        var name=_selected.ToDateTime(TimeOnly.MinValue).ToString("D",_culture);
        MonthGrid.Children.OfType<Button>().FirstOrDefault(b=>AutomationProperties.GetName(b).StartsWith(name,StringComparison.Ordinal))?.Focus(FocusState.Keyboard);
    }

    private bool IsCalendarVisible(string id) => !_settings.HiddenCalendarIds.Contains(id);

    private void AgendaScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => UpdateAgendaOverflow();

    private void AgendaScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Extent and viewport metrics settle after content has been measured.
        DispatcherQueue.TryEnqueue(UpdateAgendaOverflow);
    }

    private void UpdateAgendaOverflow()
    {
        if (MoreEventsButton is null) return;
        MoreEventsButton.Visibility = EventList.Children.Count > 0 &&
            AgendaScrollViewer.ScrollableHeight - AgendaScrollViewer.VerticalOffset > 1
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MoreEvents_Click(object sender, RoutedEventArgs e) =>
        AgendaScrollViewer.ChangeView(null,
            Math.Min(AgendaScrollViewer.ScrollableHeight,
                AgendaScrollViewer.VerticalOffset + AgendaScrollViewer.ViewportHeight * 0.8), null);

    private void RenderAgenda()
    {
        if (!_ready) return;
        SelectedDateText.Text = _selected==DateOnly.FromDateTime(DateTime.Today)?"Today":_selected.ToDateTime(TimeOnly.MinValue).ToString("m",_culture);
        var items=_events.Where(e=>IsCalendarVisible(e.CalendarId)&&CalendarMath.OccursOn(e,_selected,TimeZoneInfo.Local)).OrderByDescending(e=>e.IsAllDay).ThenBy(e=>e.Start).ToArray();
        EventList.Children.Clear();
        if (_renderedAgendaDate!=_selected) { AgendaScrollViewer.ChangeView(null,0,null,true);_renderedAgendaDate=_selected; }
        EmptyPanel.Visibility=items.Length==0?Visibility.Visible:Visibility.Collapsed;
        var connected=_provider?.AccountName is not null;
        EmptyTitle.Text=connected?"No events":"Your day, at a glance";
        EmptyMessage.Text=connected?"Nothing scheduled in the selected calendars for this day.":"Add an iCalendar feed from Google, Outlook, or another calendar service, or use a local .ics file.";
        ConnectButton.Visibility=connected?Visibility.Collapsed:Visibility.Visible;
        foreach (var item in items)
        {
            var source=_calendars.FirstOrDefault(c=>c.Id==item.CalendarId);
            var grid=new Grid { ColumnSpacing=12,Padding=new Thickness(2,8,4,8) };
            grid.ColumnDefinitions.Add(new() { Width=new GridLength(4) }); grid.ColumnDefinitions.Add(new() { Width=new GridLength(1,GridUnitType.Star) });
            var bar=new Border { Width=4,CornerRadius=new CornerRadius(2),Background=CalendarColor(source?.Color) }; grid.Children.Add(bar);
            var stack=new StackPanel { Spacing=1 }; Grid.SetColumn(stack,1); grid.Children.Add(stack);
            stack.Children.Add(new TextBlock { Text=item.Subject,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text=EventTime(item),FontSize=12,Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            stack.Children.Add(new TextBlock { Text=string.Join(" · ",new[]{source?.Name,item.Location}.Where(s=>!string.IsNullOrWhiteSpace(s))),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=(Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            var button=new Button { Content=grid,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(0),Padding=new Thickness(2) };
            button.Click += async (_,_) => await ShowDetailsAsync(item);
            EventList.Children.Add(button);
        }
        CalendarsButton.Content=$"{_calendars.Count(c=>IsCalendarVisible(c.Id))} calendars";
    }

    private string EventTime(CalendarEvent item) => item.IsAllDay?"All day":$"{TimeZoneInfo.ConvertTime(item.Start,TimeZoneInfo.Local).ToString("t",_culture)} – {TimeZoneInfo.ConvertTime(item.End,TimeZoneInfo.Local).ToString("t",_culture)}";
    private Brush CalendarColor(string? color)
    {
        try { if (color is {Length:7} && color[0]=='#') return new SolidColorBrush(Windows.UI.Color.FromArgb(255,Convert.ToByte(color.Substring(1,2),16),Convert.ToByte(color.Substring(3,2),16),Convert.ToByte(color.Substring(5,2),16))); } catch (FormatException) { }
        return (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    }

    private void RenderSettings()
    {
        RegionText.Text=$"{_culture.DisplayName} · {(_settings.UseIsoWeeks?DayOfWeek.Monday:_culture.DateTimeFormat.FirstDayOfWeek)} first · {(_settings.UseIsoWeeks?"ISO 8601":_culture.DateTimeFormat.CalendarWeekRule.ToString())}";
        SettingsAccountText.Text=_provider?.AccountName??"No calendar source connected";
        SignOutButton.IsEnabled=_provider?.AccountName is not null;
    }

    private void SetPanel(string panel)
    {
        _panel=panel;
        AutomationProperties.SetName(SettingsButton,panel=="settings"?"Close settings":"Settings");
        AutomationProperties.SetName(ManageFeedsButton,panel=="feeds"?"Close calendar feeds":"Manage calendar feeds");
        AgendaPanel.Visibility=panel=="agenda"?Visibility.Visible:Visibility.Collapsed;
        AuxiliaryScrollViewer.Visibility=panel=="agenda"?Visibility.Collapsed:Visibility.Visible;
        CalendarsPanel.Visibility=panel=="calendars"?Visibility.Visible:Visibility.Collapsed;
        EditorPanel.Visibility=panel=="editor"?Visibility.Visible:Visibility.Collapsed;
        SettingsPanel.Visibility=panel=="settings"?Visibility.Visible:Visibility.Collapsed;
        FeedsPanel.Visibility=panel=="feeds"?Visibility.Visible:Visibility.Collapsed;
        CalendarPanel.Visibility=!_collapsed && (panel=="agenda"||panel=="calendars")?Visibility.Visible:Visibility.Collapsed;
        RenderSettings(); Position();
    }

    private void Position() => _shell?.PositionFlyout(392,_panel is "settings" or "editor" or "feeds"?780:_collapsed?470:780);
    private void ShowFlyout() { _trayDismissTimer.Stop(); _visible=true; Position(); _shell?.Show(); ApplyFrame(); _clockTimer.Start(); RenderAll(); }
    private void HideFlyout() { _trayDismissTimer.Stop(); if (_shell is null) return; _visible=false; _shell.Hide(); _clockTimer.Stop(); }
    private void ToggleFlyout() { if (_shell?.IsVisible ?? _visible) HideFlyout(); else ShowFlyout(); }
    private void Status(string message, InfoBarSeverity severity=InfoBarSeverity.Informational) { StatusBar.Message=message; StatusBar.Severity=severity; StatusBar.IsOpen=true; }

    private async Task RefreshAsync()
    {
        if (_provider is null || _authBusy) { RenderAll(); return; }
        _refreshCts?.Cancel(); _refreshCts?.Dispose(); _refreshCts=new();
        var token=_refreshCts.Token; var provider=_provider;
        var rows=CalendarMath.BuildMonth(_month,_culture,_settings.UseIsoWeeks);
        var startDate=rows[0].Days[0].ToDateTime(TimeOnly.MinValue); var endDate=rows[^1].Days[^1].AddDays(1).ToDateTime(TimeOnly.MinValue);
        SyncText.Text="Refreshing…";
        var acquired=false;
        try {
            await _operationGate.WaitAsync(token); acquired=true;
            var calendars=await provider.GetCalendarsAsync(token);
            var events=await provider.GetEventsAsync(calendars,new DateTimeOffset(startDate,TimeZoneInfo.Local.GetUtcOffset(startDate)),new DateTimeOffset(endDate,TimeZoneInfo.Local.GetUtcOffset(endDate)),token);
            token.ThrowIfCancellationRequested(); if (_provider!=provider) return;
            _calendars=calendars; _events=events;
            SetRefreshStatus(provider);
            RenderAll();
        } catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) { SyncText.Text=_events.Count>0?"Offline · previous data":"Not synced"; Status(FriendlyError(ex),InfoBarSeverity.Warning); } }
        finally { if(acquired) _operationGate.Release(); }
    }

    private static string FriendlyError(Exception e) => e is HttpRequestException?"Calendar refresh failed. Check your connection and try again.":e.Message;
    private void Calendars_Click(object sender,RoutedEventArgs e)
    {
        CalendarList.Children.Clear(); AccountText.Text=_provider?.AccountName??"Connect an account in Settings to choose calendars.";
        foreach (var calendar in _calendars) {
            var check=new CheckBox { Content=calendar.Name+(calendar.CanEdit?"":" · read only"),IsChecked=IsCalendarVisible(calendar.Id) };
            check.Click += (_,_) => { if (check.IsChecked==true) _settings.HiddenCalendarIds.Remove(calendar.Id); else _settings.HiddenCalendarIds.Add(calendar.Id); _settings.Save(App.DataDirectory); RenderCalendar(); RenderAgenda(); };
            CalendarList.Children.Add(check);
        }
        SetPanel("calendars");
    }

    private void Add_Click(object sender,RoutedEventArgs e)
    {
        EventCalendar.ItemsSource=_calendars.Where(c=>c.CanEdit).ToArray(); EventCalendar.SelectedIndex=0;
        EventDate.Date=new DateTimeOffset(_selected.ToDateTime(TimeOnly.MinValue));
        var next=DateTime.Now.AddMinutes(30); StartTime.Time=new TimeSpan(next.Hour,next.Minute/15*15,0); EndTime.Time=StartTime.Time.Add(TimeSpan.FromMinutes(30));
        if (EndTime.Time>=TimeSpan.FromDays(1)) EndTime.Time=new TimeSpan(23,59,0);
        var uses12=_culture.DateTimeFormat.ShortTimePattern.Contains('h'); StartTime.ClockIdentifier=EndTime.ClockIdentifier=uses12?"12HourClock":"24HourClock";
        SetPanel("editor"); EventTitle.Focus(FocusState.Programmatic);
    }

    private async void Save_Click(object sender,RoutedEventArgs e)
    {
        if (_saving || _authBusy || _provider is null) return;
        if (string.IsNullOrWhiteSpace(EventTitle.Text) || EventCalendar.SelectedItem is not CalendarSource calendar) { Status("Enter a title and select a writable calendar.",InfoBarSeverity.Warning); return; }
        var date=EventDate.Date.Date;
        var start=AllDayToggle.IsOn?date:date.Add(StartTime.Time); var end=AllDayToggle.IsOn?date.AddDays(1):date.Add(EndTime.Time);
        if (end<=start) { Status("End time must be after start time.",InfoBarSeverity.Warning); return; }
        if (TimeZoneInfo.Local.IsInvalidTime(start)||TimeZoneInfo.Local.IsInvalidTime(end)) { Status("That time does not exist because of a daylight-saving change. Choose another time.",InfoBarSeverity.Warning); return; }
        if (TimeZoneInfo.Local.IsAmbiguousTime(start)||TimeZoneInfo.Local.IsAmbiguousTime(end)) { Status("That time occurs twice during a daylight-saving change. Use your full calendar app to choose the intended occurrence.",InfoBarSeverity.Warning); return; }
        _saving=true; _authBusy=true; SaveButton.IsEnabled=false;
        _refreshCts?.Cancel();
        await _operationGate.WaitAsync();
        try {
            await _provider.CreateEventAsync(new(calendar.Id,EventTitle.Text.Trim(),new(start,TimeZoneInfo.Local.GetUtcOffset(start)),new(end,TimeZoneInfo.Local.GetUtcOffset(end)),AllDayToggle.IsOn,EventLocation.Text,EventNotes.Text));
            _selected=DateOnly.FromDateTime(date); _month=_selected; EventTitle.Text=""; EventLocation.Text=""; EventNotes.Text=""; StatusBar.IsOpen=false; SetPanel("agenda");
        } catch (OperationCanceledException) { Status("Save cancelled. Your draft is still here."); }
        catch (Exception ex) { Status($"Event was not saved. {FriendlyError(ex)}",InfoBarSeverity.Error); }
        finally { _operationGate.Release(); _saving=false; _authBusy=false; SaveButton.IsEnabled=true; ShowFlyout(); }
        if (_panel=="agenda") await RefreshAsync();
    }

    private async Task ShowDetailsAsync(CalendarEvent item)
    {
        if (_dialogOpen) return;
        var stack=new StackPanel { Spacing=12 };
        stack.Children.Add(new TextBlock { Text=EventTime(item),TextWrapping=TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text=item.Location,TextWrapping=TextWrapping.Wrap });
        var dialog=new ContentDialog { XamlRoot=Root.XamlRoot,Title=item.Subject,Content=stack,CloseButtonText="Close",DefaultButton=ContentDialogButton.Close };
        if (ValidWebUri(item.JoinUrl)) dialog.PrimaryButtonText="Join meeting";
        if (ValidWebUri(item.WebLink)) dialog.SecondaryButtonText="Open in calendar";
        _dialogOpen=true;
        try { var result=await dialog.ShowAsync(); _dialogOpen=false; if (result==ContentDialogResult.Primary) { HideFlyout(); await OpenUri(item.JoinUrl!); } else if(result==ContentDialogResult.Secondary) { HideFlyout(); await OpenUri(item.WebLink!); } }
        finally { _dialogOpen=false; }
    }
    private static bool ValidWebUri(string? value) => Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https";
    private static async Task OpenUri(string uri) => await Launcher.LaunchUriAsync(new Uri(uri));
    private void Preferences_Changed(object sender,RoutedEventArgs e) { if (!_ready) return; var rangeChanged=_settings.UseIsoWeeks!=IsoToggle.IsOn; _settings.ShowWeekNumbers=WeekToggle.IsOn; _settings.UseIsoWeeks=IsoToggle.IsOn; _settings.InterceptClock=false; _settings.Save(App.DataDirectory); RenderAll(); if(rangeChanged) _=RefreshAsync(); }
    private void AllDay_Toggled(object sender,RoutedEventArgs e) { if (StartTime is null) return; StartTime.Visibility=EndTime.Visibility=AllDayToggle.IsOn?Visibility.Collapsed:Visibility.Visible; }
    private void Collapse_Click(object sender,RoutedEventArgs e) { _collapsed=!_collapsed; ((FontIcon)CollapseButton.Content).Glyph=_collapsed?"\uE70E":"\uE70D"; AutomationProperties.SetName(CollapseButton,_collapsed?"Expand calendar":"Collapse calendar"); SetPanel(_panel); }
    private async void Previous_Click(object sender,RoutedEventArgs e) { _month=CalendarMath.AddMonth(_month,-1,_culture); _selected=CalendarMath.MonthStart(_month,_culture); RenderAll(); await RefreshAsync(); }
    private async void Next_Click(object sender,RoutedEventArgs e) { _month=CalendarMath.AddMonth(_month,1,_culture); _selected=CalendarMath.MonthStart(_month,_culture); RenderAll(); await RefreshAsync(); }
    private async void Today_Click(object sender,RoutedEventArgs e) { _month=_selected=DateOnly.FromDateTime(DateTime.Today); RenderAll(); await RefreshAsync(); }
    private async void PreviousDay_Click(object sender,RoutedEventArgs e) => await NavigateDayAsync(-1);
    private async void NextDay_Click(object sender,RoutedEventArgs e) => await NavigateDayAsync(1);
    private async Task NavigateDayAsync(int delta)
    {
        _selected=_selected.AddDays(delta);
        var monthChanged=CalendarMath.MonthStart(_selected,_culture)!=CalendarMath.MonthStart(_month,_culture);
        if(monthChanged)_month=_selected;
        RenderAll();
        if(monthChanged)await RefreshAsync();
    }
    private void ManageFeeds_Click(object sender,RoutedEventArgs e)
    {
        if(_panel=="feeds")SetPanel("agenda");
        else Feeds_Click(sender,e);
    }
    private void Back_Click(object sender,RoutedEventArgs e) => SetPanel("agenda");
    private void Settings_Click(object sender,RoutedEventArgs e) => SetPanel(_panel=="settings"?"agenda":"settings");
    private async void Refresh_Click(object sender,RoutedEventArgs e) => await RefreshAsync();
    private async void WindowsAccounts_Click(object sender,RoutedEventArgs e) => await OpenUri("ms-settings:emailandaccounts");
    private async void RegionSettings_Click(object sender,RoutedEventArgs e) => await OpenUri("ms-settings:regionformatting");
    private async void Registration_Click(object sender,RoutedEventArgs e) => await OpenUri("https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app");
    private void Root_KeyDown(object sender,KeyRoutedEventArgs e) { if (e.Key==VirtualKey.Escape && !_dialogOpen) { e.Handled=true; if(_panel!="agenda") SetPanel("agenda"); else HideFlyout(); } }
    private void Exit_Click(object sender,RoutedEventArgs e) => ExitApp();
    private void ExitApp() { _exiting=true; _refreshCts?.Cancel(); _clockTimer.Stop(); _refreshTimer.Stop(); _trayDismissTimer.Stop(); _shell?.Dispose(); _frame?.Dispose(); (_provider as IDisposable)?.Dispose(); Application.Current.Exit(); }
}
