namespace WinFormsExampleApp;

using System.Drawing;
using System.Windows.Forms;

public sealed class MainForm : Form
{
	private readonly TextBox input;
	private readonly Label status;
	private readonly ComboBox choices;
	private readonly CheckBox enabledCheckBox;

	public MainForm()
	{
		Text = "DeepFlowTest WinForms Example";
		Name = "MainForm";
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(520, 300);

		input = new TextBox
		{
			Name = "InputTextBox",
			Text = "Ready",
			Location = new Point(16, 16),
			Width = 220,
			AccessibleName = "Input Text Box",
		};

		status = new Label
		{
			Name = "StatusLabel",
			Text = "Idle",
			Location = new Point(16, 56),
			Width = 300,
			AccessibleName = "Status Label",
		};

		var primaryButton = new Button
		{
			Name = "PrimaryButton",
			Text = "Primary Action",
			Location = new Point(260, 14),
			Width = 140,
			AccessibleName = "Primary Button",
		};
		primaryButton.Click += (_, _) => status.Text = $"Clicked: {input.Text}";

		enabledCheckBox = new CheckBox
		{
			Name = "EnabledCheckBox",
			Text = "Enabled",
			Checked = true,
			Location = new Point(16, 88),
			Width = 120,
			AccessibleName = "Enabled Check Box",
		};

		choices = new ComboBox
		{
			Name = "ChoiceComboBox",
			DropDownStyle = ComboBoxStyle.DropDownList,
			Location = new Point(150, 86),
			Width = 160,
			AccessibleName = "Choice Combo Box",
		};
		choices.Items.AddRange(new object[] { "Alpha", "Beta", "Gamma" });
		choices.SelectedIndex = 0;

		var secondaryButton = new Button
		{
			Name = "ShowSecondaryFormButton",
			Text = "Secondary Form",
			Location = new Point(16, 132),
			Width = 140,
			AccessibleName = "Show Secondary Form Button",
		};
		secondaryButton.Click += (_, _) => new SecondaryForm().Show(this);

		var modalButton = new Button
		{
			Name = "ShowModalDialogButton",
			Text = "Modal Dialog",
			Location = new Point(172, 132),
			Width = 120,
			AccessibleName = "Show Modal Dialog Button",
		};
		modalButton.Click += (_, _) =>
		{
			using var dialog = new ModalDialogForm();
			dialog.ShowDialog(this);
		};

		var fileDialogButton = new Button
		{
			Name = "ShowFileDialogButton",
			Text = "File Dialog",
			Location = new Point(308, 132),
			Width = 110,
			AccessibleName = "Show File Dialog Button",
		};
		fileDialogButton.Click += (_, _) =>
		{
			using var dialog = new OpenFileDialog { Title = "DeepFlowTest File Dialog", FileName = input.Text };
			if (dialog.ShowDialog(this) == DialogResult.OK)
				input.Text = dialog.FileName;
		};

		Controls.AddRange(new Control[]
		{
			input,
			status,
			primaryButton,
			enabledCheckBox,
			choices,
			secondaryButton,
			modalButton,
			fileDialogButton,
		});
	}
}
