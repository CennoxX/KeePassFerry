using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KeePassLib;
using KeePassFerry.Models.DataExchange;
using KeePassFerry.Models.Shared;
using KeePassFerry.Properties;

namespace KeePassFerry.Forms
{
    /// <summary>
    /// Formularfeld hinzufügen/bearbeiten. Der Wert steht im Eintrag: Benutzername, Passwort oder ein Stringfeld
    /// ("Wert aus"); ein neues Stringfeld legt der Aufrufer an (NewStringName). Nur alte Felder mit eigenem Wert
    /// (ValuePath ".") und Schalter behalten den Wert im Feld selbst (#46). Die Art des Felds ergibt sich aus dem
    /// DOM-Typ. Seite, DOM-Selektor und Platzhalter-Einstellung zeigt der Dialog nicht (die Erweiterung wertet Seite
    /// und Selektor nicht aus, Platzhalter stellt man für die Datenbank ein); gespeicherte Werte bleiben erhalten.
    /// </summary>
    public partial class FerryFieldForm : Form
    {
        public new string Name;
        public string Value;
        public string Id;
        public FieldType Type;
        public int Page;
        public PlaceholderHandling PlaceholderHandling;
        public string HtmlType;
        public string QuerySelector;
        // Herkunft des Werts: PwDefs.UserNameField, PwDefs.PasswordField, Name eines Stringfelds oder "." (im Feld)
        public string ValuePath;
        // Neues Stringfeld: sein Name (Value ist sein Wert), sonst null
        public string NewStringName;

        private const string NewStringPath = "\u0000new";

        private sealed class Source
        {
            public string Path;
            public string Text;
            public override string ToString() { return Text; }
        }

        private readonly Label labelSource = new Label { AutoSize = true };
        private readonly ComboBox comboSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label labelNewName = new Label { AutoSize = true };
        private readonly TextBox textNewName = new TextBox();
        private readonly Label labelHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        // HTML-Typ des Felds auf der Seite: Auswahl der üblichen Typen, eigene Eingabe möglich. Die Art (Text,
        // Passwort, Schalter, Auswahl) ergibt sich daraus wie in der Erweiterung (Field.typeFromDOMtype)
        private readonly ComboBox comboDomType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly FieldType originalType;
        private readonly HashSet<string> existingStrings;
        private bool loaded;
        private bool updating;

        public static FerryFieldForm FromField(Field field, IList<string> stringFields)
        {
            var customFmc = field.MatcherConfigs != null ? field.MatcherConfigs.FirstOrDefault(fmc => fmc != null && (fmc.MatcherType == null || fmc.MatcherType.GetValueOrDefault(FieldMatcherType.Custom) == FieldMatcherType.Custom)) : null;
            var cm = customFmc != null ? customFmc.CustomMatcher : null;
            string id = cm != null && cm.Ids != null ? cm.Ids.FirstOrDefault() ?? "" : "";
            string name = cm != null && cm.Names != null ? cm.Names.FirstOrDefault() ?? "" : "";
            string htmlType = cm != null && cm.Types != null ? cm.Types.FirstOrDefault() ?? "" : "";
            string querySelector = cm != null && cm.Queries != null ? cm.Queries.FirstOrDefault() ?? "" : "";

            return new FerryFieldForm(name, field.Value, id, field.Type, field.Page,
                field.PlaceholderHandling.GetValueOrDefault(PlaceholderHandling.Default), field.ValuePath, true,
                htmlType, querySelector, stringFields);
        }

        public FerryFieldForm(string name, string value, string id, FieldType type, int page, PlaceholderHandling phh,
            string valuePath, bool editing, string htmlType, string querySelector, IList<string> stringFields)
        {
            InitializeComponent();
            FerryText.Localize(this);
            Icon = Resources.FerryIcon;
            Text = FerryText.L(editing ? "Edit a form field" : "Add a form field");

            existingStrings = new HashSet<string>(stringFields ?? new List<string>());
            labelSource.Text = FerryText.T("Wert aus", "Value from");
            labelNewName.Text = FerryText.T("Stringfeld", "String field");
            Controls.AddRange(new Control[] { labelSource, comboSource, labelNewName, textNewName, labelHint, comboDomType });
            comboDomType.Items.AddRange(new object[] { "text", "password", "email", "tel", "number", "url", "search", "checkbox", "radio", "select-one" });
            originalType = type;

            comboSource.Items.Add(new Source { Path = PwDefs.UserNameField, Text = FerryText.L("KeePass username") });
            comboSource.Items.Add(new Source { Path = PwDefs.PasswordField, Text = FerryText.L("KeePass password") });
            foreach (string s in stringFields ?? new List<string>())
                comboSource.Items.Add(new Source { Path = s, Text = FerryText.T("Stringfeld: ", "String field: ") + s });
            // Verweis auf ein Stringfeld, das es im Eintrag nicht mehr gibt: trotzdem anzeigen, damit nichts verloren geht
            bool isString = !string.IsNullOrEmpty(valuePath) && valuePath != "." && valuePath != PwDefs.UserNameField && valuePath != PwDefs.PasswordField;
            if (isString && !existingStrings.Contains(valuePath))
                comboSource.Items.Add(new Source { Path = valuePath, Text = FerryText.T("Stringfeld: ", "String field: ") + valuePath + FerryText.T(" (fehlt)", " (missing)") });
            comboSource.Items.Add(new Source { Path = NewStringPath, Text = FerryText.T("Neues Stringfeld …", "New string field …") });
            // Alte Felder mit eigenem Wert bleiben bearbeitbar, neue Felder bekommen diese Wahl nicht mehr
            if (editing && valuePath == "." && type != FieldType.Toggle)
                comboSource.Items.Add(new Source { Path = ".", Text = FerryText.T("Eigener Wert (nur in KeePassFerry)", "Own value (KeePassFerry only)") });

            string selectPath = editing ? (string.IsNullOrEmpty(valuePath) ? "." : valuePath) : NewStringPath;
            foreach (Source s in comboSource.Items)
                if (s.Path == selectPath) comboSource.SelectedItem = s;
            if (comboSource.SelectedIndex < 0) comboSource.SelectedIndex = comboSource.Items.Count - 1;

            if (type == FieldType.Toggle) checkBox1.Checked = value == "KEEPASSFERRY_CHECKED_TRUE";
            else if (valuePath == ".") textBox2.Text = value;
            Value = value;

            textBox1.Text = Name = name;
            textBox3.Text = Id = id;
            // Nicht im Dialog, aber unverändert gespeichert
            Page = page;
            QuerySelector = querySelector;
            PlaceholderHandling = phh;
            HtmlType = htmlType;
            // Ältere Felder ohne HTML-Typ: aus der gespeicherten Art vorbelegen, damit sie sich beim Speichern nicht ändert
            if (string.IsNullOrEmpty(htmlType) && type == FieldType.Password) htmlType = "password";
            if (string.IsNullOrEmpty(htmlType) && type == FieldType.Toggle) htmlType = "checkbox";
            comboDomType.Text = htmlType ?? "";

            comboSource.SelectedIndexChanged += (s, e) => UpdateLayout();
            comboDomType.TextChanged += (s, e) => UpdateLayout();
        }

        private Source SelectedSource
        {
            get { return comboSource.SelectedItem as Source; }
        }

        // Art des Felds: Wert aus dem KeePass-Passwort immer Passwort, sonst nach dem HTML-Typ; ohne HTML-Typ die
        // gespeicherte Art (ältere Felder)
        private FieldType DerivedType
        {
            get
            {
                Source src = SelectedSource;
                string dom = comboDomType.Text.Trim().ToLowerInvariant();
                if (dom == "checkbox") return FieldType.Toggle;
                if (src != null && src.Path == PwDefs.PasswordField) return FieldType.Password;
                switch (dom)
                {
                    case "": return originalType == FieldType.Toggle ? FieldType.Text : originalType;
                    case "password": return FieldType.Password;
                    case "radio":
                    case "select-one": return FieldType.Existing;
                    default: return FieldType.Text;
                }
            }
        }

        private bool IsToggle
        {
            get { return DerivedType == FieldType.Toggle; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            AddToolTips();
            loaded = true;
            UpdateLayout();
            comboSource.Focus();
        }

        // Zeilen von oben nach unten: "Wert aus" (bei Schaltern das Kästchen), bei neuem Stringfeld dessen Name, bei
        // neuem Stringfeld oder eigenem Wert der Wert, sonst ein Hinweis, wo der Wert geändert wird; dann Name, ID,
        // DOM-Typ und die Knöpfe
        private void UpdateLayout()
        {
            if (!loaded || updating) return;
            updating = true;
            try
            {
                Source src = SelectedSource;
                string path = src == null ? "." : src.Path;
                bool toggle = IsToggle;
                bool newString = !toggle && path == NewStringPath;
                bool showValue = newString || (!toggle && path == ".");
                bool hint = !toggle && !showValue;

                if (hint)
                {
                    labelHint.Text = path == PwDefs.UserNameField || path == PwDefs.PasswordField
                        ? FerryText.T("Den Wert änderst du im Reiter „Allgemein“ des Eintrags.", "To change the value, use the \"General\" tab of the entry.")
                        : FerryText.T("Den Wert änderst du im Reiter „Erweitert“ des Eintrags.", "To change the value, use the \"Advanced\" tab of the entry.");
                }
                textBox2.UseSystemPasswordChar = DerivedType == FieldType.Password;

                checkBox1.Visible = toggle;
                labelSource.Visible = comboSource.Visible = !toggle;
                labelNewName.Visible = textNewName.Visible = newString;
                label2.Visible = textBox2.Visible = showValue;
                labelHint.Visible = hint;

                int margin = textBox1.Height / 2 + 2;
                int rowHeight = textBox1.Height + margin / 2 + 2;
                var labels = new Control[] { labelSource, labelNewName, label2, label1, label3, label10 };
                int labelWidth = labels.Max(l => l.PreferredSize.Width);
                int left = margin + labelWidth + margin / 2;
                int width = ClientSize.Width - left - margin;
                int y = margin;

                Action<Label, Control> row = (label, input) =>
                {
                    input.SetBounds(left, y, width, input.Height);
                    label.Left = left - margin / 2 - label.PreferredSize.Width;
                    label.Top = y + (input.Height - label.Height) / 2;
                    y += rowHeight;
                };

                if (toggle)
                {
                    checkBox1.Location = new Point(left, y);
                    y += rowHeight;
                }
                else
                {
                    row(labelSource, comboSource);
                    if (newString) row(labelNewName, textNewName);
                    if (showValue) row(label2, textBox2);
                    if (hint)
                    {
                        labelHint.MaximumSize = new Size(width, 0);
                        labelHint.Location = new Point(left, y - rowHeight / 4);
                        y = labelHint.Bottom + margin;
                    }
                }
                row(label1, textBox1);
                row(label3, textBox3);
                row(label10, comboDomType);

                y += margin / 2;
                button2.Location = new Point(ClientSize.Width - margin - button2.Width, y);
                button1.Location = new Point(button2.Left - margin / 2 - button1.Width, y);
                ClientSize = new Size(ClientSize.Width, button1.Bottom + margin);

                int tab = 0;
                foreach (Control c in new Control[] { comboSource, checkBox1, textNewName, textBox2, textBox1, textBox3, comboDomType, button1, button2 })
                    c.TabIndex = tab++;
            }
            finally
            {
                updating = false;
            }
        }

        // Erklärungen beim Zeigen auf Beschriftung oder Feld
        private void AddToolTips()
        {
            var tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400 };
            Action<string, Control[]> tip = (text, controls) => { foreach (Control c in controls) tips.SetToolTip(c, text); };
            tip(FerryText.T("Woher der Wert kommt, den KeePassFerry in das Feld auf der Webseite einträgt: Benutzername oder Passwort des Eintrags, ein Stringfeld des Eintrags (Reiter „Erweitert“) oder ein neues Stringfeld.",
                "Where the value comes from that KeePassFerry fills into the field on the web page: the entry's user name or password, a string field of the entry (\"Advanced\" tab) or a new string field."),
                new Control[] { labelSource, comboSource });
            tip(FerryText.T("Name des neuen Stringfelds im Reiter „Erweitert“. Unter diesem Namen sehen es auch andere KeePass-Programme.",
                "Name of the new string field on the \"Advanced\" tab. Other KeePass apps show it under this name."),
                new Control[] { labelNewName, textNewName });
            tip(FerryText.T("Inhalt, der in das Feld auf der Webseite eingetragen wird.", "Value that is filled into the field on the web page."),
                new Control[] { label2, textBox2 });
            tip(FerryText.T("Attribut „name“ des Eingabefelds auf der Webseite. Daran erkennt KeePassFerry das Feld wieder. Zu sehen im Browser mit Rechtsklick auf das Feld → Untersuchen.",
                "The \"name\" attribute of the input field on the web page. KeePassFerry uses it to recognise the field. In the browser: right-click the field → Inspect."),
                new Control[] { label1, textBox1 });
            tip(FerryText.T("Attribut „id“ des Eingabefelds auf der Webseite. Passt die ID, wird das Feld bevorzugt. Zu sehen im Browser mit Rechtsklick auf das Feld → Untersuchen.",
                "The \"id\" attribute of the input field on the web page. A matching id is preferred. In the browser: right-click the field → Inspect."),
                new Control[] { label3, textBox3 });
            tip(FerryText.T("HTML-Typ des Eingabefelds (Attribut „type“). Daraus ergibt sich die Art des Felds: password = Passwort, checkbox = Schalter, radio oder select-one = Auswahl, alles andere = Text. Ein neues Stringfeld wird bei password geschützt gespeichert.",
                "HTML type of the input field (\"type\" attribute). It also sets the kind of field: password = password, checkbox = toggle, radio or select-one = choice, anything else = text. A new string field is stored protected for password."),
                new Control[] { label10, comboDomType });
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Source src = SelectedSource;
            string path = IsToggle || src == null ? "." : src.Path;

            if (path == NewStringPath)
            {
                string newName = textNewName.Text.Trim();
                if (newName.Length == 0)
                {
                    MessageBox.Show(this, FerryText.T("Bitte gib einen Namen für das Stringfeld an.", "Please enter a name for the string field."), "KeePassFerry");
                    DialogResult = DialogResult.None;
                    return;
                }
                if (existingStrings.Contains(newName) || FerryService.IsReservedStringName(newName))
                {
                    MessageBox.Show(this, FerryText.T("Ein Stringfeld mit diesem Namen gibt es schon oder der Name ist reserviert. Wähle ein vorhandenes Stringfeld unter „Wert aus“ oder einen anderen Namen.",
                        "A string field with this name already exists or the name is reserved. Choose an existing string field under \"Value from\" or another name."), "KeePassFerry");
                    DialogResult = DialogResult.None;
                    return;
                }
            }

            if (textBox2.Visible && textBox2.Text.Length <= 0)
            {
                MessageBox.Show(this, FerryText.L("Please specify a value"), "KeePassFerry");
                DialogResult = DialogResult.None;
                return;
            }

            //TODO: Review in 2024/25 to see if we can remove this restriction after the migration to config v2
            if (textBox2.Visible && (textBox2.Text == "{USERNAME}" || textBox2.Text == "{PASSWORD}"))
            {
                MessageBox.Show(this,
                    FerryText.L("Please change the value of this form field. {USERNAME} and {PASSWORD} are reserved for internal use by KeePassFerry."), "KeePassFerry");
                DialogResult = DialogResult.None;
                return;
            }

            Name = textBox1.Text;
            Id = textBox3.Text;
            HtmlType = comboDomType.Text.Trim();
            Type = DerivedType;

            NewStringName = null;
            if (IsToggle)
            {
                ValuePath = ".";
                Value = checkBox1.Checked ? "KEEPASSFERRY_CHECKED_TRUE" : "KEEPASSFERRY_CHECKED_FALSE";
            }
            else if (path == NewStringPath)
            {
                NewStringName = textNewName.Text.Trim();
                ValuePath = NewStringName;
                Value = textBox2.Text;
            }
            else if (path == ".")
            {
                ValuePath = ".";
                Value = textBox2.Text;
            }
            else
            {
                ValuePath = path;
                Value = null;
            }
        }
    }
}
