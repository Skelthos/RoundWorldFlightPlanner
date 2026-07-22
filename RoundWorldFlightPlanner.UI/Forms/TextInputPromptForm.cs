namespace RoundWorldFlightPlanner.UI.Forms;

/// <summary>Generic single-line text input dialog.</summary>
public class TextInputPromptForm : Form
{
    private readonly TextBox _input;

    public string Value => _input.Text;

    public TextInputPromptForm(string title, string prompt, string initialValue = "")
    {
        Text = title;
        Width = 400;
        Height = 160;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label { Text = prompt, Dock = DockStyle.Top, Height = 30, Padding = new Padding(12, 12, 12, 0) };
        _input = new TextBox { Text = initialValue, Dock = DockStyle.Top, Margin = new Padding(12) };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(_input);
        Controls.Add(label);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}
