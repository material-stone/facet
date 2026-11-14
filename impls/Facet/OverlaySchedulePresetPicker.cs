using System.Windows.Automation;
using System.Windows.Input;
using Facet.Design;

namespace Facet;

// Anchored, always-expanded list of saved color/label presets; each row ends with an inline remove button.
internal sealed class OverlaySchedulePresetPicker : Border
{
    internal readonly ListBox PresetInput=new(){Margin=new(0),MaxHeight=264,Padding=new(4)};
    internal readonly Button AddButton=new(){ContentTemplate=null,Margin=new(0,12,0,0),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left};
    private readonly TextBlock empty=new(){Text="저장된 프리셋이 없습니다.",FontSize=13,Margin=new(0,0,0,4),Visibility=Visibility.Collapsed};
    
    internal event Action<SchedulePreset>? PresetSelected,PresetRemoved;
    internal event Action? AddRequested;

    internal OverlaySchedulePresetPicker()
    {
        Padding=new(16);CornerRadius=new(12);BorderThickness=new(1);
        Background=(Brush)FindResource("Panel");BorderBrush=(Brush)FindResource("Edge");empty.Foreground=(Brush)FindResource("Muted");
        KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);
        var body=new StackPanel();
        body.Children.Add(new TextBlock{Text="색상 · 레이블 프리셋",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12)});
        body.Children.Add(empty);body.Children.Add(PresetInput);
        AutomationProperties.SetName(PresetInput,"색상 · 레이블 프리셋 선택");
        ScrollViewer.SetVerticalScrollBarVisibility(PresetInput,ScrollBarVisibility.Auto);ScrollViewer.SetHorizontalScrollBarVisibility(PresetInput,ScrollBarVisibility.Disabled);
        // Choose on click release (also re-choosing the current row); the remove button handles its own click.
        PresetInput.PreviewMouseLeftButtonUp+=(_,e)=> {
            if(e.OriginalSource is DependencyObject source && !IsInButton(source) && ItemsControl.ContainerFromElement(PresetInput,source) is ListBoxItem {Tag:SchedulePreset preset}){e.Handled=true;Choose(preset);}
        };
        PresetInput.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space && PresetInput.SelectedItem is ListBoxItem {Tag:SchedulePreset preset}){e.Handled=true;Choose(preset);}};
        AddButton.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");AddButton.Padding=new(10,8,10,8);
        AddButton.Content=IconLabel(IconKind.Add,"새 프리셋 추가");AutomationProperties.SetName(AddButton,"새 프리셋 추가");
        AddButton.Click+=(_,_)=>AddRequested?.Invoke();body.Children.Add(AddButton);
        Child=body;
    }

    internal IEnumerable<SchedulePreset> Presets=>PresetInput.Items.OfType<ListBoxItem>().Select(row=>(SchedulePreset)row.Tag);
    internal SchedulePreset? SelectedPreset=>(PresetInput.SelectedItem as ListBoxItem)?.Tag as SchedulePreset;
    internal Button? RemoveButton(SchedulePreset preset)=>Row(preset)?.Content is DockPanel row?row.Children.OfType<Button>().FirstOrDefault():null;
    private ListBoxItem? Row(SchedulePreset preset)=>PresetInput.Items.OfType<ListBoxItem>().FirstOrDefault(row=>((SchedulePreset)row.Tag).Key==preset.Key);
    internal void Choose(SchedulePreset preset)=>PresetSelected?.Invoke(preset.Copy());
    private bool IsInButton(DependencyObject? node)
    {
        for(;node!=null && !ReferenceEquals(node,PresetInput);node=node is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is Button)return true;
        return false;
    }
    internal void SetPresets(IReadOnlyList<SchedulePreset> presets,string color,string label)
    {
        PresetInput.Items.Clear();
        string key=new SchedulePreset{Color=color,Label=label}.Key;
        foreach(var preset in presets)PresetInput.Items.Add(CreateRow(preset.Copy()));
        PresetInput.SelectedItem=PresetInput.Items.OfType<ListBoxItem>().FirstOrDefault(row=>((SchedulePreset)row.Tag).Key==key);
        UpdateEmpty();if(PresetInput.SelectedItem!=null)PresetInput.ScrollIntoView(PresetInput.SelectedItem);
    }
    internal void Remove(SchedulePreset preset)
    {
        
        if(Row(preset) is {} row)PresetInput.Items.Remove(row);
        UpdateEmpty();
    }
    private void UpdateEmpty()
    {
        bool none=PresetInput.Items.Count==0;
        empty.Visibility=none?Visibility.Visible:Visibility.Collapsed;PresetInput.Visibility=none?Visibility.Collapsed:Visibility.Visible;
    }

    // Rows are real elements: a FrameworkElementFactory child of a Button never becomes its Content, so the icon would not render.
    private ListBoxItem CreateRow(SchedulePreset preset)
    {
        var swatch=new Border{Width=16,Height=16,CornerRadius=new(4),Margin=new(0,0,10,0),VerticalAlignment=VerticalAlignment.Center,
            Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Color))};
        var remove=new Button{Content=new Icon{Kind=IconKind.Trash,Width=16,Height=16},ContentTemplate=null,Width=28,Height=28,Padding=new(6),
            Margin=new(10,-4,0,-4),BorderThickness=new(0),Background=Brushes.Transparent,Focusable=false,ToolTip="프리셋 삭제",VerticalAlignment=VerticalAlignment.Center};
        remove.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");remove.BorderBrush=Brushes.Transparent;
        AutomationProperties.SetName(remove,"프리셋 삭제 · "+preset.Label);
        remove.Click+=(_,e)=>{e.Handled=true;PresetRemoved?.Invoke(preset.Copy());};
        var text=new TextBlock{Text=preset.Label,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
        var row=new DockPanel();DockPanel.SetDock(swatch,Dock.Left);DockPanel.SetDock(remove,Dock.Right);
        row.Children.Add(swatch);row.Children.Add(remove);row.Children.Add(text);
        var item=new ListBoxItem{Content=row,Tag=preset};AutomationProperties.SetName(item,preset.Label+" "+preset.Color);return item;
    }
    internal static StackPanel IconLabel(IconKind kind,string text)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        row.Children.Add(new Icon{Kind=kind,Width=16,Height=16,Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center});
        row.Children.Add(new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center});return row;
    }
}

// Modal form for a new preset: color box (opens the shared color picker) and label, confirmed or canceled explicitly.
internal sealed class OverlaySchedulePresetDialog : Border
{
    internal readonly TextBox ColorInput=new(){MaxLength=7,IsReadOnly=true,Cursor=Cursors.Hand,Margin=new(0),Padding=new(38,10,12,10)};
    internal readonly TextBox LabelInput=new(){MaxLength=40,Margin=new(0)};
    internal OverlayScheduleColorPicker ColorPicker {get;}=new(){Visibility=Visibility.Collapsed,Margin=new(0,12,0,0)};
    internal readonly Button ConfirmButton=new(){Content="확인",Margin=new(8,0,0,0)},CancelButton=new(){Content="취소",Margin=new(0)};
    internal bool IsColorPickerOpen {get;private set;}
    internal Func<SchedulePreset,bool>? Confirm;
    internal event Action? Canceled;
    private readonly Border colorPreview=new(){Width=18,Height=18,CornerRadius=new(4),BorderThickness=new(1),Margin=new(12,0,0,0),
        HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand};
    private readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0),Visibility=Visibility.Collapsed};

    internal OverlaySchedulePresetDialog()
    {
        Padding=new(16);CornerRadius=new(12);BorderThickness=new(1);
        Background=(Brush)FindResource("Panel");BorderBrush=(Brush)FindResource("Edge");colorPreview.BorderBrush=BorderBrush;error.Foreground=(Brush)FindResource("Danger");
        KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);
        foreach(var input in new[]{ColorInput,LabelInput})input.SetResourceReference(StyleProperty,"TextBoxWithoutFocusOutline");
        ConfirmButton.SetResourceReference(StyleProperty,"Primary");
        var body=new StackPanel();
        body.Children.Add(new TextBlock{Text="새 프리셋",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12)});
        var fields=new Grid();fields.ColumnDefinitions.Add(new(){Width=new(120)});fields.ColumnDefinitions.Add(new(){Width=new(10)});fields.ColumnDefinitions.Add(new());
        var colorField=new Grid();colorField.Children.Add(ColorInput);colorField.Children.Add(colorPreview);fields.Children.Add(colorField);
        Grid.SetColumn(LabelInput,2);fields.Children.Add(LabelInput);body.Children.Add(fields);
        body.Children.Add(ColorPicker);body.Children.Add(error);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,16,0,0)};
        actions.Children.Add(CancelButton);actions.Children.Add(ConfirmButton);body.Children.Add(actions);
        Child=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        AutomationProperties.SetName(ColorInput,"프리셋 색상 #RRGGBB");AutomationProperties.SetName(LabelInput,"프리셋 레이블");
        ColorInput.TextChanged+=(_,_)=>{if(OverlayScheduleColorPicker.TryColor(ColorInput.Text,out var color))colorPreview.Background=new SolidColorBrush(color);};
        foreach(var target in new FrameworkElement[]{ColorInput,colorPreview}) {
            target.PreviewMouseLeftButtonDown+=(_,e)=>{e.Handled=true;ColorInput.Focus();};
            target.PreviewMouseLeftButtonUp+=(_,e)=>{e.Handled=true;ToggleColorPicker();};
        }
        ColorInput.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){e.Handled=true;ToggleColorPicker();}};
        ColorPicker.ColorChanged+=color=>ColorInput.Text=color;
        LabelInput.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && Keyboard.Modifiers==ModifierKeys.None){e.Handled=true;Submit();}};
        ConfirmButton.Click+=(_,_)=>Submit();CancelButton.Click+=(_,_)=>Canceled?.Invoke();
    }

    internal void Open(string color)
    {
        CloseColorPicker();ColorInput.Text=OverlayScheduleColorPicker.TryColor(color,out _)?color.ToUpperInvariant():"#82CFAD";LabelInput.Text="";
        error.Visibility=Visibility.Collapsed;LabelInput.Focus();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,()=>{if(IsVisible)LabelInput.Focus();});
    }
    internal void ToggleColorPicker(){if(IsColorPickerOpen)CloseColorPicker();else OpenColorPicker();}
    internal void OpenColorPicker()
    {
        if(IsColorPickerOpen)return;
        ColorPicker.SetColor(ColorInput.Text);IsColorPickerOpen=true;ColorPicker.Visibility=Visibility.Visible;
        ColorPicker.HexInput.Focus();ColorPicker.HexInput.SelectAll();
    }
    internal void CloseColorPicker()
    {
        if(!IsColorPickerOpen)return;
        IsColorPickerOpen=false;ColorPicker.Visibility=Visibility.Collapsed;if(IsVisible)ColorInput.Focus();
    }
    internal bool Submit()
    {
        var preset=new SchedulePreset{Color=ColorInput.Text.Trim().ToUpperInvariant(),Label=LabelInput.Text.Trim()};
        if(!SchedulePreset.IsValid(preset)){ShowError("레이블(1~40자)과 색상을 확인해 주세요.");return false;}
        error.Visibility=Visibility.Collapsed;
        if(Confirm?.Invoke(preset)!=true){if(error.Visibility!=Visibility.Visible)ShowError("프리셋을 저장하지 못했습니다. 다시 시도해 주세요.");return false;}
        return true;
    }
    internal void ShowError(string message){error.Text=message;error.Visibility=Visibility.Visible;}
}
