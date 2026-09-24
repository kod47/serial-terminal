using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SerialTerminal.Core;

namespace SerialTerminal;

public partial class SessionView : UserControl
{
    private SessionViewModel? _session;
    private ScrollViewer? _scroll;
    private bool _stickToBottom = true;

    public SessionView()
    {
        InitializeComponent();
        Terminal.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopySelection()));
        DataContextChanged += (_, _) => Attach(DataContext as SessionViewModel);
        Loaded += (_, _) =>
        {
            _scroll ??= FindChild<ScrollViewer>(Terminal);
            if (_scroll != null)
            {
                _scroll.ScrollChanged -= Scroll_ScrollChanged;
                _scroll.ScrollChanged += Scroll_ScrollChanged;
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            Dispatcher.BeginInvoke(() => FindChild<TextBox>(SendLinesPanel, t => t.Name == "SendBox")?.Focus());
        };
    }

    private void Attach(SessionViewModel? session)
    {
        if (_session != null)
        {
            _session.OutputChanged -= FollowOutput;
            _session.PropertyChanged -= Session_PropertyChanged;
            _session.Main.PropertyChanged -= Main_PropertyChanged;
        }
        _session = session;
        if (session == null) return;
        session.OutputChanged += FollowOutput;
        session.PropertyChanged += Session_PropertyChanged;
        session.Main.PropertyChanged += Main_PropertyChanged;
        UpdateGraphLayout();
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.ShowGraph)) UpdateGraphLayout();
        if (e.PropertyName == nameof(SessionViewModel.FilterEnabled) && _session?.FilterEnabled == true && IsVisible)
        {
            _session.Main.ShowFilterBar = true;
            // the box becomes visible first, then it can take focus
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => FilterBox.Focus());
        }
    }

    private void Main_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AutoScroll) && _session?.Main.AutoScroll == true)
        {
            _stickToBottom = true;
            _scroll?.ScrollToEnd();
        }
    }

    private void UpdateGraphLayout()
    {
        bool show = _session?.ShowGraph == true;
        GraphRow.Height = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        GraphSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        GraphPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // Auto-scroll follows new data only while the view is at the bottom;
    // scrolling up pauses it, scrolling back to the bottom resumes it.
    private void Scroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scroll == null || _session == null) return;
        if (e.VerticalChange != 0)
            _stickToBottom = _scroll.VerticalOffset >= _scroll.ScrollableHeight - _session.Main.FontSize * 1.5;
        else if (e.ViewportHeightChange != 0)
            FollowOutput();
    }

    private void FollowOutput()
    {
        if (_stickToBottom && _session?.Main.AutoScroll == true) _scroll?.ScrollToEnd();
    }

    private void Terminal_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_session == null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        _session.Main.FontSize = Math.Clamp(_session.Main.FontSize + (e.Delta > 0 ? 1 : -1), 8, 48);
        e.Handled = true;
    }

    private void CopySelection()
    {
        if (_session == null || Terminal.SelectedItems.Count == 0) return;
        var selected = new HashSet<object>(Terminal.SelectedItems.Cast<object>());
        var sb = new StringBuilder();
        foreach (var line in _session.Lines)
            if (selected.Contains(line)) sb.AppendLine(_session.FormatForCopy(line));
        Clipboard.SetText(sb.ToString());
    }

    private void SendBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session == null || sender is not TextBox { DataContext: SendLine line } box) return;
        switch (e.Key)
        {
            case Key.Enter:
                _session.SendCommand.Execute(line);
                break;
            case Key.Up:
                _session.HistoryUp(line);
                box.CaretIndex = box.Text.Length;
                break;
            case Key.Down:
                _session.HistoryDown(line);
                box.CaretIndex = box.Text.Length;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Min/Max apply on Enter (and when the box loses focus)
    private void GraphRange_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void MacroButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Macro macro }) _session?.Main.EditMacro(macro);
        e.Handled = true;
    }

    private static T? FindChild<T>(DependencyObject parent, Func<T, bool>? match = null) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found && (match == null || match(found))) return found;
            if (FindChild(child, match) is { } nested) return nested;
        }
        return null;
    }
}
