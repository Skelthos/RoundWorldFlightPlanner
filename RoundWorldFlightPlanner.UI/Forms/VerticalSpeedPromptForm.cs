namespace RoundWorldFlightPlanner.UI.Forms;

/// <summary>Small modal asking for the touchdown vertical speed, like Volanta's landing-rate prompt.</summary>
public class VerticalSpeedPromptForm : Form
{
    private readonly NumericUpDown _vsInput;

    public double VerticalSpeedFpm => (double)_vsInput.Value;

    public VerticalSpeedPromptForm()
    {
        Text = "Landing Rate";
        Width = 360;
        Height = 170;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "Touchdown vertical speed (fpm, negative = descending):",
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 12, 12, 0),
        };

        _vsInput = new NumericUpDown
        {
            Minimum = -3000,
            Maximum = 0,
            Value = -250,
            Increment = 10,
            Dock = DockStyle.Top,
            Margin = new Padding(12),
        };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK };
        buttonPanel.Controls.Add(okButton);

        Controls.Add(_vsInput);
        Controls.Add(label);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
    }
}
