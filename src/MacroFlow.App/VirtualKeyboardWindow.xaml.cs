using System.Windows;
using System.Windows.Controls;
using MacroFlow.App.Input;

namespace MacroFlow.App;

public partial class VirtualKeyboardWindow : Window
{
    public VirtualKeyboardWindow(string purpose)
    {
        InitializeComponent();
        PurposeText.Text = purpose;

        AddKeys(FunctionKeysPanel, Enumerable.Range(1, 24).Select(number => $"F{number}"));
        AddKeys(MainKeysPanel,
            Enumerable.Range('1', 9).Select(code => ((char)code).ToString())
                .Append("0")
                .Concat(Enumerable.Range('A', 26).Select(code => ((char)code).ToString()))
                .Concat(["SPACE", "TAB", "ENTER", "BACKSPACE", "ESCAPE"]));
        AddKeys(ControlKeysPanel,
        [
            "LSHIFT", "RSHIFT", "LCTRL", "RCTRL", "LALT", "RALT",
            "CAPSLOCK", "PRINTSCREEN", "PAUSE", "INSERT", "DELETE", "HOME", "END",
            "PAGEUP", "PAGEDOWN", "LEFT", "UP", "DOWN", "RIGHT",
            "VK_BA", "VK_BB", "VK_BC", "VK_BD", "VK_BE", "VK_BF", "VK_C0",
            "VK_DB", "VK_DC", "VK_DD", "VK_DE", "VK_E2"
        ]);
        AddKeys(NumpadKeysPanel,
            Enumerable.Range(0x60, 10).Select(KeyMap.ToToken)
                .Concat(["VK_6A", "VK_6B", "VK_6D", "VK_6E", "VK_6F", "NUMLOCK"]));
    }

    public string? SelectedToken { get; private set; }

    private void AddKeys(System.Windows.Controls.Panel panel, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = KeyMap.GetDisplayName(token),
                Tag = token,
                MinWidth = 58,
                Height = 42,
                Margin = new Thickness(4),
                Padding = new Thickness(10, 5, 10, 5)
            };
            button.Click += Key_Click;
            panel.Children.Add(button);
        }
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string token }) return;
        SelectedToken = token;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
