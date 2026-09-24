using System.ComponentModel;
using System.Windows;

namespace SerialTerminal;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        _vm = new MainViewModel(); // applies the theme before controls are created
        InitializeComponent();
        DataContext = _vm;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // Items of "Edit macro" are generated from the macro list; the click bubbles up here.
    private void EditMacroMenu_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: Core.Macro macro }) _vm.EditMacro(macro);
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _vm.Shutdown();
}
