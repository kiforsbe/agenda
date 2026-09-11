using Agenda.Providers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Agenda.App;

public sealed partial class MainWindow
{
    private string? _feedDragOrigin;
    private bool _feedDragActive;
    private void ConfigureFeedRowDragging(Grid row,FeedConfiguration feed)
    {
        AutomationProperties.SetName(row,feed.Name);
        AutomationProperties.SetHelpText(row,"Drag to reorder, or focus the entry and press Alt+Up or Alt+Down.");
        // Observe handled presses too, so the row's action buttons disarm dragging.
        row.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=>{
            var source=e.OriginalSource as DependencyObject;
            var onButton=false;
            while(source is not null && !ReferenceEquals(source,row)) {
                if(source is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) { onButton=true;break; }
                source=VisualTreeHelper.GetParent(source);
            }
            _feedDragOrigin=!_authBusy && !onButton &&
                e.GetCurrentPoint(row).Properties.IsLeftButtonPressed ? feed.Id : null;
        }),true);
        row.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,_)=>{
            if(!_feedDragActive)_feedDragOrigin=null;
        }),true);
    }

    private async void FeedList_KeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(e.Key is not (VirtualKey.Up or VirtualKey.Down) ||
            !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))return;
        if(_authBusy || _feedDragActive)return;
        var focused=FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        while(focused is not null && focused is not ListViewItem)
            focused=VisualTreeHelper.GetParent(focused);
        if(focused is not ListViewItem { Content: Grid row } || !FeedList.Items.Contains(row))return;
        e.Handled=true;
        var index=FeedList.Items.IndexOf(row);
        var target=index+(e.Key==VirtualKey.Up?-1:1);
        if(target<0 || target>=FeedList.Items.Count)return;
        FeedList.Items.RemoveAt(index);
        FeedList.Items.Insert(target,row);
        FeedList.ScrollIntoView(row);
        await SaveFeedOrderAsync();
        if(_panel=="feeds" && FeedList.ContainerFromItem(row) is ListViewItem container)
            container.Focus(FocusState.Keyboard);
    }

    private void FeedList_DragItemsStarting(object sender,DragItemsStartingEventArgs args)
    {
        // The ListView supplies native insertion gaps, drag visuals, and edge scrolling.
        // The entry is draggable; its color, edit, and delete buttons remain controls.
        if(_authBusy || args.Items.Count!=1 || args.Items[0] is not Grid row ||
            _feedDragOrigin is null || !Equals(row.Tag,_feedDragOrigin)) {
            args.Cancel=true;
            _feedDragOrigin=null;
            return;
        }
        _feedDragActive=true;
        _authBusy=true;
        args.Data.RequestedOperation=DataPackageOperation.Move;
    }

    private async void FeedList_DragItemsCompleted(ListViewBase sender,DragItemsCompletedEventArgs args)
    {
        if(!_feedDragActive)return;
        _feedDragActive=false;
        _feedDragOrigin=null;
        _authBusy=false;
        if(args.DropResult!=DataPackageOperation.Move) {
            RestoreFeedOrder();
            return;
        }
        await SaveFeedOrderAsync();
    }

    private async Task SaveFeedOrderAsync()
    {
        _authBusy=true;
        try {
            var ids=FeedList.Items.Cast<Grid>().Select(row=>(string)row.Tag).ToArray();
            if(ids.SequenceEqual(_feeds.Select(feed=>feed.Id)))return;
            var ordered=FeedConfigurationOrder.Apply(_feeds,ids);
            await FeedConfigurationStore.SaveAsync(App.DataDirectory,ordered);
            _feeds=ordered;
            // Keep the existing rows, focus, and scroll position. No calendar download.
        }
        catch(Exception ex) {
            RestoreFeedOrder();
            Status("Could not save feed order. "+FriendlyError(ex),InfoBarSeverity.Warning);
        }
        finally { _authBusy=false; }
    }

    private void RestoreFeedOrder()
    {
        // Move existing rows back without clearing the list or losing their controls.
        for(var target=0;target<_feeds.Count;target++) {
            var row=FeedList.Items.Cast<Grid>().First(item=>Equals(item.Tag,_feeds[target].Id));
            var current=FeedList.Items.IndexOf(row);
            if(current==target)continue;
            FeedList.Items.RemoveAt(current);
            FeedList.Items.Insert(target,row);
        }
    }
}
