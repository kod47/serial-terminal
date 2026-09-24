using System.Windows;
using SerialTerminal.Core;

namespace SerialTerminal;

public partial class MacroEditWindow : Window
{
    private readonly Macro _macro;

    public MacroEditWindow(Macro macro)
    {
        InitializeComponent();
        _macro = macro;
        NameBox.Text = macro.Name;
        DataBox.Text = macro.Data;
        HexBox.IsChecked = macro.IsHex;
        CrcBox.IsChecked = macro.AppendCrc;
        Loaded += (_, _) => DataBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        bool hex = HexBox.IsChecked == true;
        if (hex)
        {
            try
            {
                HexParser.Parse(DataBox.Text);
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _macro.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _macro.Name : NameBox.Text.Trim();
        _macro.Data = DataBox.Text;
        _macro.IsHex = hex;
        _macro.AppendCrc = hex && CrcBox.IsChecked == true;
        DialogResult = true;
    }
}
