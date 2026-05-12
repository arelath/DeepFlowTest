namespace WinFormsExampleApp;

using System.Drawing;
using System.Windows.Forms;

public sealed class ModalDialogForm : Form
{
	public ModalDialogForm()
	{
		Text = "DeepFlowTest Modal WinForms Dialog";
		Name = "ModalDialogForm";
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(360, 180);
		AcceptButton = CreateButton("OkButton", "OK", DialogResult.OK, new Point(176, 120));
		CancelButton = CreateButton("CancelButton", "Cancel", DialogResult.Cancel, new Point(256, 120));

		Controls.Add(new Label
		{
			Name = "ModalDialogLabel",
			Text = "Modal dialog content",
			Location = new Point(16, 16),
			Width = 240,
			AccessibleName = "Modal Dialog Label",
		});
		Controls.Add((Control)AcceptButton);
		Controls.Add((Control)CancelButton);
	}

	private static Button CreateButton(string name, string text, DialogResult result, Point location) =>
		new()
		{
			Name = name,
			Text = text,
			DialogResult = result,
			Location = location,
			Width = 72,
			AccessibleName = text + " Button",
		};
}
