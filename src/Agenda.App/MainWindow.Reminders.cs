using Agenda.App.Services;
using Agenda.Core;
using Agenda.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Agenda.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _reminderTimer=new() { Interval=TimeSpan.FromSeconds(10) };
    private MeetingNotificationService? _notifications;
    private Task _connectionsReady=Task.CompletedTask;

    private void InitializeReminders()
    {
        _notifications=new MeetingNotificationService(key=>DispatcherQueue.TryEnqueue(async ()=> {
            try {
                await _connectionsReady;
                ShowFlyout();
                var item=_events.FirstOrDefault(item=>MeetingReminders.Key(_settings.SourceMode,item)==key);
                if(item is not null)await OpenSourceCalendarAsync(item);
                else Status("This event is no longer available in the active calendars.");
            }
            catch { Status("The event could not be opened. Refresh your calendars and try again."); }
        }));
        _reminderTimer.Tick+=(_,_)=>CheckReminders();
        _reminderTimer.Start();
        if(!_settings.MeetingNotifications)_=_notifications.ClearAsync();
        UpdateNotificationStatus();
    }

    private void UpdateNotificationStatus()
    {
        SetNotificationStatus(_notifications?.Error ??
            (_settings.MeetingNotifications && _notifications?.Available!=true
                ? "Notifications are blocked or unavailable in Windows. Check Settings > System > Notifications."
                : ""));
    }

    private void SetNotificationStatus(string message)
    {
        NotificationStatus.Text=message;
        NotificationStatus.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
    }

    private void CheckReminders()
    {
        if(_exiting || !_settings.MeetingNotifications || _authBusy || !_connectionsReady.IsCompletedSuccessfully)return;
        UpdateNotificationStatus();
        if(_notifications?.Available!=true)return;
        var now=DateTimeOffset.UtcNow;
        foreach(var key in _settings.NotifiedMeetings.Where(pair=>pair.Value<now.AddDays(-1)).Select(pair=>pair.Key).ToArray())
            _settings.NotifiedMeetings.Remove(key);
        foreach(var key in _settings.EventReminders.Where(pair=>pair.Value.ExpiresAt<now.AddDays(-1)).Select(pair=>pair.Key).ToArray())
            _settings.EventReminders.Remove(key);
        var visible=_calendars.Where(calendar=>IsCalendarVisible(calendar.Id)).Select(calendar=>calendar.Id).ToHashSet();
        foreach(var reminder in MeetingReminders.Due(_events,visible,_settings.NotifiedMeetings,
            _settings.SourceMode,now,_settings.ReminderMinutesBefore,_settings.EventReminders)) {
            // Persist before delivery: restarting cannot replay this occurrence/offset.
            _settings.NotifiedMeetings[reminder.DeliveryKey]=reminder.Event.End;
            try {
                _settings.Save(App.DataDirectory);
                var calendar=_calendars.First(c=>c.Id==reminder.Event.CalendarId);
                var sound=ReminderSounds.Resolve(_settings.ReminderSound,_settings.EventReminders.GetValueOrDefault(reminder.EventKey)?.Sound);
                if(!_notifications.Show(reminder,EventTime(reminder.Event),calendar.Name,sound))
                    SetNotificationStatus("Windows did not accept the notification. Check Windows notification settings.");
            }
            catch {
                // Keep the in-memory ledger so failures cannot create a notification loop.
                SetNotificationStatus("The reminder could not be saved or delivered. Check Windows notification settings and restart Agenda.");
            }
        }
    }

    private static void BuildReminderChoices(StackPanel panel,IEnumerable<int> selected,Action changed)
    {
        var values=selected.ToHashSet();
        foreach(var minutes in MeetingReminders.LeadTimes) {
            var choice=new CheckBox { Content=minutes==0?"At start":minutes==60?"1 hour before":$"{minutes} minutes before",
                Tag=minutes,IsChecked=values.Contains(minutes),HorizontalAlignment=HorizontalAlignment.Left };
            choice.Checked+=(_,_)=>changed();choice.Unchecked+=(_,_)=>changed();
            panel.Children.Add(choice);
        }
    }

    private static List<int> SelectedReminderTimes(StackPanel panel) => panel.Children.OfType<CheckBox>()
        .Where(box=>box.IsChecked==true).Select(box=>(int)box.Tag).ToList();

    private static void EnableReminderChoices(StackPanel panel,bool enabled)
    {
        foreach(var box in panel.Children.OfType<CheckBox>())box.IsEnabled=enabled;
    }

    private void SaveReminderPreferences()
    {
        try { _settings.Save(App.DataDirectory); }
        catch { Status("Reminder preferences could not be saved. Check access to the Agenda settings folder."); }
    }

    private void SaveDefaultReminderTimes()
    {
        if(!_ready)return;
        _settings.ReminderMinutesBefore=SelectedReminderTimes(ReminderTimesPanel);
        SaveReminderPreferences();
    }

    private async void Notifications_Changed(object sender,RoutedEventArgs e)
    {
        if(!_ready)return;
        _settings.MeetingNotifications=NotificationToggle.IsOn;
        EnableReminderChoices(ReminderTimesPanel,NotificationToggle.IsOn);
        NotificationSound.IsEnabled=NotificationToggle.IsOn;
        SaveReminderPreferences();UpdateNotificationStatus();
        if(!NotificationToggle.IsOn && _notifications is not null)await _notifications.ClearAsync();
    }

    private static void BuildSoundChoices(ComboBox combo,string? selected,bool allowDefault)
    {
        if(allowDefault)combo.Items.Add(new ComboBoxItem { Content="Use default sound",Tag="" });
        foreach(var sound in ReminderSounds.Choices)
            combo.Items.Add(new ComboBoxItem { Content=sound switch {
                "Default"=>"Windows default", "IM"=>"Instant message", "SMS"=>"Text message", _=>sound
            },Tag=sound });
        combo.SelectedItem=combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==(selected??""))??combo.Items[0];
    }

    private static string? SelectedSound(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag:string sound } && sound.Length>0 ? sound : null;

    private void NotificationSound_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!_ready)return;
        _settings.ReminderSound=SelectedSound(NotificationSound)??"Default";
        SaveReminderPreferences();
    }

    private Uri? SourceWebsite(CalendarEvent item) => _provider is IcsCalendarProvider
        && _feeds.FirstOrDefault(feed=>feed.Id==item.CalendarId) is { } feed ? FeedCalendarLinks.Resolve(feed):null;

    private void AddEventReminderOptions(StackPanel content,CalendarEvent item)
    {
        if(item.IsAllDay)return;
        var key=MeetingReminders.Key(_settings.SourceMode,item);
        var custom=_settings.EventReminders.GetValueOrDefault(key);
        var panel=new StackPanel { Spacing=6 };
        var useCustom=new ToggleSwitch { IsOn=custom is not null,MinWidth=0,VerticalAlignment=VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(useCustom,"Use custom reminders");
        var choices=new StackPanel { Spacing=4,HorizontalAlignment=HorizontalAlignment.Left };
        var sound=new ComboBox { Header="Notification sound",HorizontalAlignment=HorizontalAlignment.Stretch,IsEnabled=custom is not null };
        BuildSoundChoices(sound,custom?.Sound,true);
        void Save() {
            if(!useCustom.IsOn)_settings.EventReminders.Remove(key);
            else _settings.EventReminders[key]=new(SelectedReminderTimes(choices),item.End,SelectedSound(sound));
            SaveReminderPreferences();
            if(_notifications is not null)_=_notifications.ClearAsync(key);
        }
        BuildReminderChoices(choices,custom?.MinutesBefore??_settings.ReminderMinutesBefore,Save);
        EnableReminderChoices(choices,custom is not null);
        sound.SelectionChanged+=(_,_)=>Save();
        useCustom.Toggled+=(_,_)=>{ EnableReminderChoices(choices,useCustom.IsOn);sound.IsEnabled=useCustom.IsOn;Save(); };
        panel.Children.Add(new TextBlock { Text="Use custom reminders" });
        var row=new Grid { ColumnSpacing=12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        row.Children.Add(useCustom);
        var times=new DropDownButton {
            Content="Reminder times",HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,
            Flyout=new Flyout { Content=choices,Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom }
        };
        Grid.SetColumn(times,1);row.Children.Add(times);
        panel.Children.Add(row);
        panel.Children.Add(sound);
        if(!_settings.MeetingNotifications)panel.Children.Add(new TextBlock { Text="Meeting notifications are off in Settings.",TextWrapping=TextWrapping.Wrap,FontSize=12 });
        content.Children.Add(panel);
    }
}
