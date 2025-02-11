﻿using PublishContent.Classes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Windows.Forms;

namespace PublishContent
{
	public partial class MainForm : Form
	{
		//powershell objects
		private RunspaceConfiguration rsc = null;

		private Runspace rs = null;
		private PowerShell ps = null;
		private List<Tuple<int, string>> appImages = new List<Tuple<int, string>>();
		private List<DeliveryGroup> deliveryGroups = new List<DeliveryGroup>();

		public MainForm()
		{
			InitializeComponent();

			//setup the environment for using the
			//citrix powershell cmdlets
			setupPowershellEnvironment();
			loadCitrixCmdlets();

			loadDeliveryGroups();

			loadBrokerIcons();
			//show existing broker icons
			loadListViewIcons(lvBrokerIcons);
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			cbDeliveryGroup.DataSource = deliveryGroups;
			cbDeliveryGroup.DisplayMember = "Name";
			cbDeliveryGroup.ValueMember = "Uid";

			comboBox1.DataSource = deliveryGroups;
			comboBox1.DisplayMember = "Name";
			comboBox1.ValueMember = "Uid";
		}

		#region Custom methods for loading citrix powershell cmdlets

		private void setupPowershellEnvironment()
		{
			//add the citrix powershell snapin
			rsc = RunspaceConfiguration.Create();

			rs = RunspaceFactory.CreateRunspace(rsc);
			rs.Open();

			ps = PowerShell.Create();
			ps.Runspace = rs;
		}

		private void loadCitrixCmdlets()
		{
			PSSnapInException psEx = null;

			//load all the citrix powershell snapins
			rsc.AddPSSnapIn("Citrix.ADIdentity.Admin.V2", out psEx);
			rsc.AddPSSnapIn("Citrix.Analytics.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.AppLibrary.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.AppV.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.Broker.Admin.V2", out psEx);
			rsc.AddPSSnapIn("Citrix.Configuration.Admin.V2", out psEx);
			rsc.AddPSSnapIn("Citrix.ConfigurationLogging.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.DelegatedAdmin.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.EnvTest.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.Host.Admin.V2", out psEx);
			rsc.AddPSSnapIn("Citrix.Licensing.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.MachineCreation.Admin.V2", out psEx);
			rsc.AddPSSnapIn("Citrix.Monitor.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.Orchestration.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.Storefront.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.Trust.Admin.V1", out psEx);
			rsc.AddPSSnapIn("Citrix.UserProfileManager.Admin.V1", out psEx);
		}

		#endregion Custom methods for loading citrix powershell cmdlets

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
			var icons = ps.Invoke();

			//loop through each icon returned
			foreach (var icon in icons)
			{
				//get the b64 encoded icon
				var iconB64Data = icon.Properties["EncodedIconData"].Value;
				//get the UID value
				var uidOfIcon = icon.Properties["uid"].Value;

				//conver the base 64 icon into a actual icon
				var appIcon = convertB64ToIcon(iconB64Data.ToString());
				//add the icon into the imagelist with the uuid as the key
				ilImages.Images.Add(uidOfIcon.ToString(), appIcon);

				////add icon to list view with the UID as the text
				//lvIcons.Items.Add(uidOfIcon.ToString(), ilImages.Images.Count - 1);
			}
		}

		private Icon convertPngToIcon(string Filename)
		{
			//create new guid for icon filename
			var icoFilename = $@"{Application.StartupPath}\{Guid.NewGuid().ToString()}.ico";
			Classes.Helpers.ImageConverter.ConvertToIco(Filename, icoFilename, 64);

			var appIcon = Icon.ExtractAssociatedIcon(icoFilename);

			//delete the temp created icon
			File.Delete(icoFilename);

			return appIcon;
		}

		private int addIconToBroker(string IconBase64)
		{
			//add icon to the broker
			ps.Commands.Clear();
			ps.AddCommand("New-BrokerIcon");
			ps.AddParameter("EncodedIconData", IconBase64);

			var addedIcon = ps.Invoke();

			//get the objects uuid returned
			return Convert.ToInt32(addedIcon[0].Properties["uid"].Value);
		}

		private Icon convertB64ToIcon(string icon)
		{
			byte[] iconBytes = Convert.FromBase64String(icon);

			MemoryStream iconMs = new MemoryStream(iconBytes);

			return new Icon(iconMs);
		}

		private string convertIcontoB64(Icon AppIcon)
		{
			byte[] bytes;
			using (var ms = new MemoryStream())
			{
				AppIcon.Save(ms);
				bytes = ms.ToArray();
			}
			var base64String = Convert.ToBase64String(bytes);

			return base64String;
		}

		private void loadDeliveryGroups()
		{
			//clear all the commands out of the powershell object
			ps.Commands.Clear();

			//add the cmdlet to the powershell obect
			ps.Commands.AddCommand("Get-BrokerDesktopGroup");
			//call the cmdlet
			var desktopGroups = ps.Invoke();

			//loop through each of the delivery groups returned
			foreach (var desktopGroup in desktopGroups)
			{
				try
				{
					//create a delivery group object
					DeliveryGroup dGroup = new DeliveryGroup();
					dGroup.Name = desktopGroup.Properties["Name"].Value.ToString();
					dGroup.PublishedName = desktopGroup.Properties["PublishedName"].Value.ToString();
					dGroup.Description = desktopGroup.Properties["Description"].Value.ToString();
					dGroup.UUID = Guid.Parse(desktopGroup.Properties["UUID"].Value.ToString());
					dGroup.Uid = int.Parse(desktopGroup.Properties["uid"].Value.ToString());

					deliveryGroups.Add(dGroup);
				}
				catch
				{
				}
			}
		}

		#endregion custom helper methods

		private void tsbAdd_Click(object sender, EventArgs e)
		{
			enableNewContentControls();

			loadListViewIcons(lvIcons);
		}

		private void tsbUploadImage_Click(object sender, EventArgs e)
		{
			OpenFileDialog iconDlg = new OpenFileDialog();

			if (iconDlg.ShowDialog() == DialogResult.OK)
			{
				var icon = convertPngToIcon(iconDlg.FileName);

				var b64Icon = convertIcontoB64(icon);

				var uidOfIcon = addIconToBroker(b64Icon);

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
				var newApp = ps.Invoke();
			}
			catch (System.Exception publishError)
			{
				MessageBox.Show(publishError.Message, "Publish Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

				ps.Invoke();
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

			var icons = ps.Invoke();
			foreach (var icon in icons)
			{
				try
				{
					appImages.Add(new Tuple<int, string>(
						Convert.ToInt32(icon.Properties["Uid"].Value),
						icon.Properties["EncodedIconData"].Value.ToString()))
				;
				}
				catch
				{ }
			}

			ps.Commands.Clear();

			ps.AddCommand("Get-BrokerApplication");
			ps.AddParameter("ApplicationType", "PublishedContent");

			var existingApps = ps.Invoke();

			foreach (var app in existingApps)
			{
				try
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
				catch
				{ }
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

			if (iconDlg.ShowDialog() == DialogResult.OK)
			{
				var icon = convertPngToIcon(iconDlg.FileName);

				var b64Icon = convertIcontoB64(icon);

				var uidOfIcon = addIconToBroker(b64Icon);

				ilImages.Images.Add(uidOfIcon.ToString(), icon);

				//add icon to list view
				lvIcons.Items.Add(uidOfIcon.ToString(), ilImages.Images.Count - 1);
			}
		}

		private void tsbUpdateExisting_Click(object sender, EventArgs e)
		{
			if (lbExistingContent.SelectedItems.Count == 1)
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
				ps.Invoke();
			}
		}
	}
}