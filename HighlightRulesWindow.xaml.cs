using System.Collections.ObjectModel;
using System.Windows;
using SerialTerminal.Core;

namespace SerialTerminal;

public partial class HighlightRulesWindow : Window
{
    private readonly ObservableCollection<HighlightRule> _rules;

    public HighlightRulesWindow(ObservableCollection<HighlightRule> rules)
    {
        InitializeComponent();
        _rules = rules;
        RulesList.ItemsSource = rules;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => _rules.Add(new HighlightRule());

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HighlightRule rule }) _rules.Remove(rule);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
