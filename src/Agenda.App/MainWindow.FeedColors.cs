using Agenda.Core;
using Agenda.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Agenda.App;

public sealed partial class MainWindow
{
    private string _newFeedColor = FeedColorPalette.Colors[0].Hex;

    private FrameworkElement CreateDaySourceMarker(IReadOnlyList<CalendarSource> sources)
    {
        var width=Math.Min(30,sources.Count*6);
        var segments=new StackPanel { Orientation=Orientation.Horizontal,Height=6,
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        for(var i=0;i<sources.Count;i++) {
            segments.Children.Add(new Border {
                Width=(double)width/sources.Count,Height=6,Background=CalendarColor(sources[i].Color),
                CornerRadius=new CornerRadius(i==0?3:0,i==sources.Count-1?3:0,i==sources.Count-1?3:0,i==0?3:0)
            });
        }
        return segments;
    }

    private Grid CreateFeedPalette(string selected, Action<string> choose)
    {
        var grid=new Grid { ColumnSpacing=4,RowSpacing=4 };
        const int columns=6;
        for(var i=0;i<columns;i++) grid.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        for(var i=0;i<(FeedColorPalette.Colors.Count+columns-1)/columns;i++) grid.RowDefinitions.Add(new() { Height=GridLength.Auto });
        foreach(var (color,index) in FeedColorPalette.Colors.Select((color,index)=>(color,index))) {
            var button=new ToggleButton {
                Width=40,Height=36,MinWidth=0,MinHeight=0,Padding=new Thickness(6),
                Tag=color.Hex,IsChecked=string.Equals(selected,color.Hex,StringComparison.OrdinalIgnoreCase),
                Content=new Ellipse { Width=20,Height=20,Fill=CalendarColor(color.Hex),
                    Stroke=(Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"],StrokeThickness=1 }
            };
            AutomationProperties.SetName(button,color.Name);
            ToolTipService.SetToolTip(button,color.Name);
            button.Click+=(_,_)=>{
                foreach(var other in grid.Children.OfType<ToggleButton>()) other.IsChecked=ReferenceEquals(other,button);
                choose(color.Hex);
            };
            Grid.SetRow(button,index/columns);Grid.SetColumn(button,index%columns);grid.Children.Add(button);
        }
        return grid;
    }

    private Button CreateFeedColorButton(FeedConfiguration feed)
    {
        var button=new Button { Width=32,Height=32,MinWidth=0,MinHeight=0,Padding=new Thickness(4),
            Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),BorderThickness=new Thickness(0),
            Content=new Ellipse { Width=20,Height=20,Fill=CalendarColor(feed.Color),
                Stroke=(Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"],StrokeThickness=1 } };
        var colorName=FeedColorPalette.Colors.FirstOrDefault(c=>string.Equals(c.Hex,feed.Color,StringComparison.OrdinalIgnoreCase))?.Name??feed.Color;
        AutomationProperties.SetName(button,$"Change color for {feed.Name}, {colorName}");
        ToolTipService.SetToolTip(button,$"Change color · {colorName}");
        var flyout=new Flyout();
        var content=new StackPanel { Spacing=8 };
        content.Children.Add(new TextBlock { Text="Feed color",FontWeight=Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(CreateFeedPalette(feed.Color,async color=>{
            flyout.Hide();await SetFeedColorAsync(feed.Id,color);
        }));
        flyout.Content=content;button.Flyout=flyout;
        return button;
    }

    private async Task SetFeedColorAsync(string id,string color)
    {
        if(_authBusy || !_feeds.Any(f=>f.Id==id && f.Color!=color))return;
        _authBusy=true;_refreshCts?.Cancel();await _operationGate.WaitAsync();
        try {
            var updated=_feeds.Select(feed=>feed.Id==id?feed with { Color=color }:feed).ToArray();
            await FeedConfigurationStore.SaveAsync(App.DataDirectory,updated);
            _feeds=updated;
            // Color-only edits update the display immediately without downloading calendars.
            if(_provider is IcsCalendarProvider previous) {
                _provider=new IcsCalendarProvider(_feeds,App.DataDirectory);
                previous.Dispose();
                _calendars=_calendars.Select(calendar=>calendar.Id==id?calendar with { Color=color }:calendar).ToArray();
            }
            RenderFeeds();RenderAll();
        }
        catch(Exception ex) { Status(FriendlyError(ex),InfoBarSeverity.Warning); }
        finally { _operationGate.Release();_authBusy=false; }
    }
}
