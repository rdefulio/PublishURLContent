using PublishContent.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PublishContent
{
    public partial class MainForm : Form
    {
        Runspace rs = null;
        PowerShell ps = null;
        List<Tuple<int, string>> appImages = new List<Tuple<int, string>>();
        List<DeliveryGroup> deliveryGroups = new List<DeliveryGroup>();

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                setupPowershellEnvironment();
                loadCitrixCmdlets();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Failed to load the Citrix Broker PowerShell SDK." + Environment.NewLine + Environment.NewLine +
                    FormatException(ex) + Environment.NewLine + Environment.NewLine +
                    "Run this 64-bit build on a Delivery Controller or machine with Citrix Studio / the PowerShell SDK installed, using an account that is a Citrix administrator.",
                    "Unable to start Publish Content",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Close();
                return;
            }

            try
            {
                loadDeliveryGroups();
                loadBrokerIcons();
                loadListViewIcons(lvBrokerIcons);

                cbDeliveryGroup.DataSource = deliveryGroups;
                cbDeliveryGroup.DisplayMember = "Name";
                cbDeliveryGroup.ValueMember = "Uid";

                comboBox1.DataSource = deliveryGroups;
                comboBox1.DisplayMember = "Name";
                comboBox1.ValueMember = "Uid";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "The Broker SDK loaded, but reading delivery groups or icons failed." + Environment.NewLine + Environment.NewLine +
                    FormatException(ex),
                    "Publish Content",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static string FormatException(Exception ex)
        {
            var text = ex.ToString();
            var runtime = ex as RuntimeException;
            if (runtime == null)
                runtime = ex.InnerException as RuntimeException;
            if (runtime != null && runtime.ErrorRecord != null)
            {
                text += Environment.NewLine + Environment.NewLine + runtime.ErrorRecord.ToString();
                if (runtime.ErrorRecord.InvocationInfo != null)
                    text += Environment.NewLine + runtime.ErrorRecord.InvocationInfo.PositionMessage;
            }
            return text;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (ps != null)
            {
                ps.Dispose();
                ps = null;
            }
            if (rs != null)
            {
                rs.Dispose();
                rs = null;
            }
            base.OnFormClosed(e);
        }

        #region Custom methods for loading citrix powershell cmdlets
        private void setupPowershellEnvironment()
        {
            var iss = InitialSessionState.CreateDefault();
            iss.ThrowOnRunspaceOpenError = true;
            rs = RunspaceFactory.CreateRunspace(iss);
            rs.Open();
            ps = PowerShell.Create();
            ps.Runspace = rs;
        }

        private void loadCitrixCmdlets()
        {
            // Probe snap-ins/modules by listing (no -Name lookups). Named Get-PSSnapin / Get-Module
            // calls set PowerShell.HadErrors even with -ErrorAction Ignore, which made startup fail
            // with an empty "The Citrix PowerShell command failed" message.
            ps.Commands.Clear();
            ps.AddScript(@"
                $snapinsLoaded = @(Get-PSSnapin | ForEach-Object { $_.Name })
                $snapinsRegistered = @(Get-PSSnapin -Registered | ForEach-Object { $_.Name })
                $status = 'unloaded'

                if ($snapinsLoaded -contains 'Citrix.Broker.Admin.V2') {
                    $status = 'already:snapin:Citrix.Broker.Admin.V2'
                }
                elseif ($snapinsRegistered -contains 'Citrix.Broker.Admin.V2') {
                    Add-PSSnapin -Name 'Citrix.Broker.Admin.V2'
                    $status = 'loaded:snapin:Citrix.Broker.Admin.V2'
                }
                elseif ($snapinsRegistered -contains 'Citrix.Broker.Admin.V1') {
                    Add-PSSnapin -Name 'Citrix.Broker.Admin.V1'
                    $status = 'loaded:snapin:Citrix.Broker.Admin.V1'
                }
                else {
                    $mod = @(Get-Module -ListAvailable |
                        Where-Object { $_.Name -eq 'Citrix.Broker.Commands' -or $_.Name -eq 'Citrix.Broker.Admin.V2' } |
                        Select-Object -First 1)
                    if ($mod.Count -gt 0) {
                        Import-Module -Name $mod[0].Path
                        $status = ""loaded:module:$($mod[0].Name)""
                    }
                    else {
                        $dlls = @(
                            ""$env:ProgramFiles\Citrix\Broker\Snapin\v2\BrokerSnapin.dll"",
                            ""$env:ProgramFiles\Citrix\Broker\Snapin\Citrix.Broker.Admin.V2\Citrix.Broker.Admin.V2.dll""
                        )
                        $dll = @($dlls | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
                        if ($dll.Count -gt 0) {
                            Import-Module -Name $dll[0]
                            $status = ""loaded:dll:$($dll[0])""
                        }
                        else {
                            throw ""The Citrix Broker PowerShell SDK was not found. Registered snap-ins: $($snapinsRegistered -join ', ')""
                        }
                    }
                }

                $cmd = Get-Command -Name Get-BrokerDesktopGroup -CommandType Cmdlet,Function,Alias -ErrorAction SilentlyContinue
                if (-not $cmd) {
                    throw ""Get-BrokerDesktopGroup is not available after $status""
                }
                ""ok:$status""
            ");

            var result = InvokePs(false);
            var output = string.Join(Environment.NewLine, result.Select(o => Convert.ToString(o.BaseObject ?? o)));
            if (output.IndexOf("ok:", StringComparison.OrdinalIgnoreCase) < 0)
            {
                var details = FormatErrorStream();
                throw new InvalidOperationException(
                    "Could not initialize the Citrix Broker SDK." + Environment.NewLine +
                    (string.IsNullOrWhiteSpace(output) ? string.Empty : output + Environment.NewLine) +
                    (string.IsNullOrWhiteSpace(details) ? string.Empty : details));
            }
        }

        private Collection<PSObject> InvokePs(bool throwOnErrors = true)
        {
            ps.Streams.Error.Clear();
            Collection<PSObject> results;
            try
            {
                results = ps.Invoke();
            }
            catch (RuntimeException rex)
            {
                var details = FormatErrorRecord(rex.ErrorRecord);
                if (string.IsNullOrWhiteSpace(details))
                    details = rex.Message;
                throw new InvalidOperationException(details, rex);
            }

            if (throwOnErrors && ps.HadErrors)
            {
                var message = FormatErrorStream();
                if (string.IsNullOrWhiteSpace(message))
                {
                    if (results != null && results.Count > 0)
                        return results;
                    message = "The Citrix PowerShell command failed with no error details (HadErrors=true).";
                }
                throw new InvalidOperationException(message);
            }
            return results;
        }

        private string FormatErrorStream()
        {
            return string.Join(Environment.NewLine, ps.Streams.Error.Select(FormatErrorRecord).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static string FormatErrorRecord(ErrorRecord err)
        {
            if (err == null)
                return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(err.FullyQualifiedErrorId))
                parts.Add(err.FullyQualifiedErrorId);
            parts.Add(err.CategoryInfo.ToString());
            if (err.Exception != null)
                parts.Add(err.Exception.ToString());
            else if (!string.IsNullOrWhiteSpace(err.ToString()))
                parts.Add(err.ToString());
            if (err.InvocationInfo != null && !string.IsNullOrWhiteSpace(err.InvocationInfo.PositionMessage))
                parts.Add(err.InvocationInfo.PositionMessage);
            return string.Join(Environment.NewLine, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static object GetPsPropertyValue(PSObject obj, string name)
        {
            if (obj == null)
                return null;
            var prop = obj.Properties[name];
            return prop == null ? null : prop.Value;
        }

        private static string GetPsPropertyString(PSObject obj, string name)
        {
            var value = GetPsPropertyValue(obj, name);
            return value == null ? string.Empty : Convert.ToString(value);
        }
        #endregion

        #region custom helper methods
        private void enableNewContentControls()
        {

            loadBrokerIcons();
            cbDeliveryGroup.Enabled = true;
            tbContentURL.Enabled = true;
            tbDescription.Enabled = true;
            tbDisplayName.Enabled = true;
            lvIcons.Enabled = true;

        }
        private void disableNewContentControls()
        {
            cbDeliveryGroup.Enabled = false;
            tbContentURL.Enabled = false;
            tbDescription.Enabled = false;
            tbDisplayName.Enabled = false;
            lvIcons.Enabled = false;
        }

        private void loadListViewIcons(ListView lv)
        {
            foreach (var imageKey in ilImages.Images.Keys)
            {
                //add icon to list view with the UID as the text
                lv.Items.Add(imageKey, imageKey);
            }
        }
        private void loadBrokerIcons()
        {
            //clear all images out of the imagelist control
            ilImages.Images.Clear();
            lvIcons.Items.Clear();

            //clear all commands in the powershell object
            ps.Commands.Clear();
            ps.AddCommand("Get-BrokerIcon");
            //call the cmdlet
            var icons = InvokePs();

            //loop through each icon returned
            foreach (var icon in icons)
            {
                var iconB64Data = GetPsPropertyString(icon, "EncodedIconData");
                var uidOfIcon = GetPsPropertyString(icon, "Uid");
                if (string.IsNullOrWhiteSpace(iconB64Data) || string.IsNullOrWhiteSpace(uidOfIcon))
                    continue;

                try
                {
                    var appIcon = convertB64ToIcon(iconB64Data);
                    ilImages.Images.Add(uidOfIcon, appIcon);
                }
                catch (Exception)
                {
                    // Skip broker icons that are not valid .ico payloads.
                }
            }
        }

        private Icon iconFromIcoBytes(byte[] icoBytes)
        {
            using (var ms = new MemoryStream(icoBytes, false))
            using (var tempIcon = new Icon(ms))
            {
                return (Icon)tempIcon.Clone();
            }
        }

        private int addIconToBroker(string IconBase64)
        {
            //add icon to the broker
            ps.Commands.Clear();
            ps.AddCommand("New-BrokerIcon");
            ps.AddParameter("EncodedIconData", IconBase64);

            var addedIcon = InvokePs();

            //get the objects uuid returned
            return Convert.ToInt32(addedIcon[0].Properties["uid"].Value);
        }
        private Icon convertB64ToIcon(string icon)
        {
            byte[] iconBytes = Convert.FromBase64String(icon);

            MemoryStream iconMs = new MemoryStream(iconBytes);

            return new Icon(iconMs);
        }

        private void loadDeliveryGroups()
        {
            ps.Commands.Clear();
            ps.Commands.AddCommand("Get-BrokerDesktopGroup");
            var desktopGroups = InvokePs();

            deliveryGroups.Clear();
            foreach (var desktopGroup in desktopGroups)
            {
                var name = GetPsPropertyString(desktopGroup, "Name");
                var uidText = GetPsPropertyString(desktopGroup, "Uid");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uidText))
                    continue;

                var uuidText = GetPsPropertyString(desktopGroup, "UUID");
                Guid uuid;
                if (!Guid.TryParse(uuidText, out uuid))
                    uuid = Guid.Empty;

                int uid;
                if (!int.TryParse(uidText, out uid))
                    continue;

                deliveryGroups.Add(new DeliveryGroup
                {
                    Name = name,
                    PublishedName = GetPsPropertyString(desktopGroup, "PublishedName"),
                    Description = GetPsPropertyString(desktopGroup, "Description"),
                    UUID = uuid,
                    Uid = uid
                });
            }
        }

        #endregion

        private void tsbAdd_Click(object sender, EventArgs e)
        {
            enableNewContentControls();

            loadListViewIcons(lvIcons);
        }

        private void tsbUploadImage_Click(object sender, EventArgs e)
        {
            OpenFileDialog iconDlg = new OpenFileDialog();
            iconDlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All files|*.*";

            if (iconDlg.ShowDialog() == DialogResult.OK)
            {
                var icoBytes = Classes.Helpers.ImageConverter.ConvertImageToIcoBytes(iconDlg.FileName);
                var icon = iconFromIcoBytes(icoBytes);
                var uidOfIcon = addIconToBroker(Convert.ToBase64String(icoBytes));

                ilImages.Images.Add(uidOfIcon.ToString(), icon);

                //add icon to list view
                lvBrokerIcons.Items.Add(uidOfIcon.ToString(), ilImages.Images.Count - 1);
            }
        }

        private void tsbPublish_Click(object sender, EventArgs e)
        {
            ps.Commands.Clear();

            ps.Commands.AddCommand("New-BrokerApplication");
            ps.AddParameter("ApplicationType", "PublishedContent");
            ps.AddParameter("Name", tbDisplayName.Text);
            ps.AddParameter("CommandLineExecutable", tbContentURL.Text);
            ps.AddParameter("Description", tbDescription.Text);
            ps.AddParameter("DesktopGroup", ((DeliveryGroup)(cbDeliveryGroup.SelectedItem)).Name);
            try
            {
                var newApp = InvokePs();
            }
            catch ( System.Exception publishError )
            {
                MessageBox.Show(publishError.Message, "Publish Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (lvIcons.SelectedItems != null)
            {
                ps.Commands.Clear();
                ps.AddCommand("Set-BrokerApplication");
                ps.AddParameter("Name", tbDisplayName.Text);
                if (lvIcons.SelectedItems.Count > 0)
                {
                    ps.AddParameter("IconUid", lvIcons.SelectedItems[0].Text);
                }

                InvokePs();
            }

            disableNewContentControls();

        }

        private void tsbListContent_Click(object sender, EventArgs e)
        {
            loadDeliveryGroups();
            lbExistingContent.Items.Clear();
            //list of existing published content
            ps.Commands.Clear();

            ps.AddCommand("Get-BrokerIcon");

            var icons = InvokePs();
            foreach (var icon in icons)
            {
                appImages.Add(new Tuple<int, string>(
                    Convert.ToInt32(icon.Properties["Uid"].Value),
                    icon.Properties["EncodedIconData"].Value.ToString()))
                ;
            }

            ps.Commands.Clear();

            ps.AddCommand("Get-BrokerApplication");
            ps.AddParameter("ApplicationType", "PublishedContent");

            var existingApps = InvokePs();

            foreach (var app in existingApps)
            {
                var desktopGroups = (int[])app.Properties["AssociatedDesktopGroupUids"].Value;

                PublishedContent listApp = new PublishedContent()
                {
                    name = app.Properties["Name"].Value.ToString(),
                    browsername = app.Properties["BrowserName"].Value.ToString(),
                    commandlineexec = app.Properties["CommandLineExecutable"].Value.ToString(),
                    commandlineargs = (app.Properties["CommandLineArguments"].Value == null) ? "" : app.Properties["CommandLineArguments"].Value.ToString(),
                    description = (app.Properties["Description"].Value == null) ? "" : app.Properties["Description"].Value.ToString(),
                    associateddesktopgroupuids = (desktopGroups == null) ? 0 : desktopGroups[0],
                    iconuid = (app.Properties["IconUid"].Value == null) ? 0 : Convert.ToInt32(app.Properties["IconUid"].Value)
                };

                var iconB64 = appImages.Where(a => a.Item1 == listApp.iconuid)
                    .FirstOrDefault().Item2;

                var icon = convertB64ToIcon(iconB64);
                listApp.icon = icon.ToBitmap();

                lbExistingContent.Items.Add(listApp);

            }
        }

        private void lbExistingContent_Click(object sender, EventArgs e)
        {
            
        }

        private void lbExistingContent_SelectedIndexChanged(object sender, EventArgs e)
        {
            var publishedContent = (PublishedContent)lbExistingContent.SelectedItem;
            tbExistingContentURL.Text = publishedContent.commandlineexec;
            tbExistingDesc.Text = publishedContent.description;
            tbExistingDisplayName.Text = publishedContent.name;

            var delGroup = deliveryGroups.Where(group => group.Uid == publishedContent.associateddesktopgroupuids)
                .FirstOrDefault();

            var selectedDesktopGroupIndex = comboBox1.Items.IndexOf(delGroup);

            comboBox1.SelectedIndex = selectedDesktopGroupIndex;
            if (cbAppIcon.Items.Count == 0)
            {
                foreach (var imageKey in ilImages.Images.Keys)
                {
                    cbAppIcon.Items.Add(imageKey);
                }
            }

            var selectedIndex = cbAppIcon.Items.IndexOf(publishedContent.iconuid.ToString());
            cbAppIcon.SelectedIndex = selectedIndex;
        }
        private void cbAppIcon_SelectedIndexChanged(object sender, EventArgs e)
        {
            var iconB64 = appImages.Where(a => a.Item1 == Convert.ToInt32(cbAppIcon.SelectedItem))
                .FirstOrDefault().Item2;

            var icon = convertB64ToIcon(iconB64);

            pbAppIcon.Image = icon.ToBitmap();
        }
        private void btnAddNewImage_Click(object sender, EventArgs e)
        {
            OpenFileDialog iconDlg = new OpenFileDialog();
            iconDlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All files|*.*";

            if (iconDlg.ShowDialog() == DialogResult.OK)
            {
                var icoBytes = Classes.Helpers.ImageConverter.ConvertImageToIcoBytes(iconDlg.FileName);
                var icon = iconFromIcoBytes(icoBytes);
                var uidOfIcon = addIconToBroker(Convert.ToBase64String(icoBytes));

                ilImages.Images.Add(uidOfIcon.ToString(), icon);

                //add icon to list view
                lvIcons.Items.Add(uidOfIcon.ToString(), ilImages.Images.Count - 1);
            }
        }

        private void tsbUpdateExisting_Click(object sender, EventArgs e)
        {
            if ( lbExistingContent.SelectedItems.Count == 1)
            {
                var displayName = tbExistingDisplayName.Text;
                var description = tbExistingDesc.Text;
                var contentUrl = tbExistingContentURL.Text;
                var commandLineArgs = tbExistingCLA.Text;
                var desktopGroup = ((DeliveryGroup)comboBox1.SelectedItem).Uid;
                var appIcon = cbAppIcon.Text;

                ps.Commands.Clear();
                ps.Commands.AddCommand("Set-BrokerApplication");
                ps.AddParameter("Name", displayName);
                ps.AddParameter("CommandLineExecutable", contentUrl);
                ps.AddParameter("CommandLineArguments", commandLineArgs);
                ps.AddParameter("Description", description);
                ps.AddParameter("IconUid", appIcon);

                //cannot change the desktop group. In order to change the desktop group
                //you need to remove the application and re-add
                //ps.AddParameter("DesktopGroup", ((DeliveryGroup)(cbDeliveryGroup.SelectedItem)).Name);
                InvokePs();
            }
        }
    }
}
