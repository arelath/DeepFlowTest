namespace WinFormsExampleApp;

using System.Drawing;
using System.Windows.Forms;

public sealed class SecondaryForm : Form
{
	public SecondaryForm()
	{
		Text = "DeepFlowTest Secondary WinForms Form";
		Name = "SecondaryForm";
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(320, 160);
		Controls.Add(new Label
		{
			Name = "SecondaryFormLabel",
			Text = "Secondary form content",
			Location = new Point(16, 16),
			Width = 220,
			AccessibleName = "Secondary Form Label",
		});
	}
}
