using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SerialTerminal.Core;

namespace SerialTerminal;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private ScrollViewer? _scroll;
    private bool _stickToBottom = true;

    public MainWindow()
    {
        _vm = new MainViewModel(); // applies the theme before controls are created
        InitializeComponent();
        DataContext = _vm;

        Terminal.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopySelection()));
        _vm.OutputChanged += FollowOutput;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.AutoScroll) && _vm.AutoScroll) ScrollToEnd();
        };

        Loaded += (_, _) =>
        {
            _scroll = FindChild<ScrollViewer>(Terminal);
            if (_scroll != null) _scroll.ScrollChanged += Scroll_ScrollChanged;
            FindChild<TextBox>(SendLinesPanel)?.Focus();
        };
    }

    // Auto-scroll follows new data only while the view is at the bottom;
    // scrolling up pauses it, scrolling back to the bottom resumes it.
    private void Scroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scroll == null) return;
        if (e.VerticalChange != 0)
            _stickToBottom = _scroll.VerticalOffset >= _scroll.ScrollableHeight - _vm.FontSize * 1.5;
        else if (e.ViewportHeightChange != 0)
            FollowOutput();
    }

    private void FollowOutput()
    {
        if (_stickToBottom && _vm.AutoScroll) _scroll?.ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        _stickToBottom = true;
        _scroll?.ScrollToEnd();
    }

    private void Terminal_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        _vm.FontSize = Math.Clamp(_vm.FontSize + (e.Delta > 0 ? 1 : -1), 8, 48);
        e.Handled = true;
    }

    private void CopySelection()
    {
        if (Terminal.SelectedItems.Count == 0) return;
        var selected = new HashSet<object>(Terminal.SelectedItems.Cast<object>());
        var sb = new StringBuilder();
        foreach (var line in _vm.Lines)
            if (selected.Contains(line)) sb.AppendLine(_vm.FormatForCopy(line));
        Clipboard.SetText(sb.ToString());
    }

    private void SendBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: SendLine line } box) return;
        switch (e.Key)
        {
            case Key.Enter:
                _vm.SendCommand.Execute(line);
                break;
            case Key.Up:
                _vm.HistoryUp(line);
                box.CaretIndex = box.Text.Length;
                break;
            case Key.Down:
                _vm.HistoryDown(line);
                box.CaretIndex = box.Text.Length;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void MacroButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Macro macro }) _vm.EditMacro(macro);
        e.Handled = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _vm.Shutdown();

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
