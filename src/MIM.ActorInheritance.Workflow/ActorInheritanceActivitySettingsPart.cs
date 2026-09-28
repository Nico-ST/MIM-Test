// -----------------------------------------------------------------------------
// Author : N.Stillhart
// Date   : 2026-09-28
// Purpose: MIM Portal UI for the "Actor Inheritance" activity. Lets an
//          administrator configure the activity in the workflow designer.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI.WebControls;
using System.Workflow.ComponentModel;
using Microsoft.IdentityManagement.WebUI.Controls;
using Microsoft.ResourceManagement.Workflow.Activities;

namespace MIM.ActorInheritance.Workflow
{
    /// <summary>
    /// Activity settings part (Portal UI) for <see cref="ActorInheritanceActivity"/>.
    /// </summary>
    public class ActorInheritanceActivitySettingsPart : ActivitySettingsPart
    {
        private const string ObjectTypeTextBox = "txtIdentityObjectType";
        private const string MainIdentityAttributeTextBox = "txtMainIdentityAttribute";
        private const string InheritedAttributesTextBox = "txtInheritedAttributes";

        public override string Title
        {
            get { return "Actor Inheritance"; }
        }

        public override Activity GenerateActivityOnWorkflow(SequentialWorkflow workflow)
        {
            if (!this.ValidateInputs())
            {
                return null;
            }

            return new ActorInheritanceActivity
            {
                IdentityObjectType = this.GetText(ObjectTypeTextBox),
                MainIdentityAttribute = this.GetText(MainIdentityAttributeTextBox),
                InheritedAttributes = FormatAttributeList(this.GetText(InheritedAttributesTextBox))
            };
        }

        public override void LoadActivitySettings(Activity activity)
        {
            ActorInheritanceActivity actorInheritance = activity as ActorInheritanceActivity;
            if (actorInheritance == null)
            {
                return;
            }

            this.SetText(ObjectTypeTextBox, actorInheritance.IdentityObjectType);
            this.SetText(MainIdentityAttributeTextBox, actorInheritance.MainIdentityAttribute);
            this.SetText(InheritedAttributesTextBox, actorInheritance.InheritedAttributes);
        }

        public override ActivitySettingsPartData PersistSettings()
        {
            ActivitySettingsPartData data = new ActivitySettingsPartData();
            data["IdentityObjectType"] = this.GetText(ObjectTypeTextBox);
            data["MainIdentityAttribute"] = this.GetText(MainIdentityAttributeTextBox);
            data["InheritedAttributes"] = this.GetText(InheritedAttributesTextBox);
            return data;
        }

        public override void RestoreSettings(ActivitySettingsPartData data)
        {
            if (data == null)
            {
                return;
            }

            this.SetText(ObjectTypeTextBox, (string)data["IdentityObjectType"]);
            this.SetText(MainIdentityAttributeTextBox, (string)data["MainIdentityAttribute"]);
            this.SetText(InheritedAttributesTextBox, (string)data["InheritedAttributes"]);
        }

        public override void SwitchMode(ActivitySettingsPartMode mode)
        {
            bool readOnly = mode == ActivitySettingsPartMode.View;
            this.SetReadOnly(ObjectTypeTextBox, readOnly);
            this.SetReadOnly(MainIdentityAttributeTextBox, readOnly);
            this.SetReadOnly(InheritedAttributesTextBox, readOnly);
        }

        public override bool ValidateInputs()
        {
            IList<string> errors = InheritanceCalculator.ValidateConfiguration(
                this.GetText(ObjectTypeTextBox),
                this.GetText(MainIdentityAttributeTextBox),
                InheritanceCalculator.ParseAttributeList(this.GetText(InheritedAttributesTextBox)));
            return errors.Count == 0;
        }

        protected override void CreateChildControls()
        {
            Table layout = new Table
            {
                Width = Unit.Percentage(100.0),
                BorderWidth = 0,
                CellPadding = 2
            };

            layout.Rows.Add(this.CreateTextBoxRow("Identity object type:", ObjectTypeTextBox, false, ActorInheritanceActivity.DefaultIdentityObjectType));
            layout.Rows.Add(this.CreateTextBoxRow("Main identity attribute (reference on the sub-identity):", MainIdentityAttributeTextBox, false, ActorInheritanceActivity.DefaultMainIdentityAttribute));
            layout.Rows.Add(this.CreateTextBoxRow("Inherited actor attributes (one per line or separated by ';'):", InheritedAttributesTextBox, true, string.Empty));

            this.Controls.Add(layout);
            base.CreateChildControls();
        }

        private static string FormatAttributeList(string value)
        {
            return string.Join(";", InheritanceCalculator.ParseAttributeList(value).ToArray());
        }

        private TableRow CreateTextBoxRow(string labelText, string textBoxId, bool multiLine, string defaultValue)
        {
            Label label = new Label { Text = labelText, CssClass = this.LabelCssClass };
            TextBox textBox = new TextBox
            {
                ID = textBoxId,
                CssClass = this.TextBoxCssClass,
                Text = defaultValue,
                MaxLength = multiLine ? 4000 : 128,
                Width = 400
            };

            if (multiLine)
            {
                textBox.TextMode = TextBoxMode.MultiLine;
                textBox.Rows = 6;
                textBox.Wrap = true;
            }

            TableCell labelCell = new TableCell();
            labelCell.Controls.Add(label);
            TableCell textBoxCell = new TableCell();
            textBoxCell.Controls.Add(textBox);

            TableRow row = new TableRow();
            row.Cells.Add(labelCell);
            row.Cells.Add(textBoxCell);
            return row;
        }

        private string GetText(string textBoxId)
        {
            TextBox textBox = (TextBox)this.FindControl(textBoxId);
            return (textBox.Text ?? string.Empty).Trim();
        }

        private void SetText(string textBoxId, string text)
        {
            TextBox textBox = (TextBox)this.FindControl(textBoxId);
            textBox.Text = text ?? string.Empty;
        }

        private void SetReadOnly(string textBoxId, bool readOnly)
        {
            TextBox textBox = (TextBox)this.FindControl(textBoxId);
            textBox.ReadOnly = readOnly;
        }
    }
}
