﻿using Android.App;
using Android.Net;
using Android.OS;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeeKee.Adapters;
using VeeKee.Ssh;
using Android.Views;
using Android.Content;
using Android.Support.V7.App;
using Android.Support.V7.Widget;
using Android.Support.Design.Widget;
using AlertDialog = Android.Support.V7.App.AlertDialog;

namespace VeeKee
{
    [Activity(Label = "VeeKee", MainLauncher = true, Icon = "@drawable/VeeKeeIcon")]
    public class MainActivity : AppCompatActivity
    {
        private Android.Support.V7.Widget.Toolbar _toolbar;

        private VeeKeePreferences _preferences;

        private WifiChecker _wifiChecker;
        private string _updatingMessage;
        private string _updatedMessage;

        #region Activity Events
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set up default Shared Preferences (setting defaults if required)
            _preferences = new VeeKeePreferences(Application.Context);

            this.SetContentView(Resource.Layout.Main);

            _toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(_toolbar);

            var vpnListView = FindViewById<ListView>(Resource.Id.vpnListView);
            vpnListView.ItemClick += new EventHandler<AdapterView.ItemClickEventArgs>(VpnListView_ItemClick);
        }

        async protected override void OnResume()
        {
            base.OnResume();

            if (_preferences.FirstRun)
            {
                // Direct user to settings
                var preferencesIntent = new Intent(Application.Context, typeof(AppPreferencesActivity));
                StartActivity(preferencesIntent);

                _preferences.FirstRun = false;
                _preferences.Save();
                return;
            }

            await InitialiseMainScreen();
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.settingsmenu, menu);
            return base.OnCreateOptionsMenu(menu);
        }
        #endregion Activity Events

        #region Click Events
        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.settings_button:
                    var preferencesIntent = new Intent(this, typeof(AppPreferencesActivity));
                    StartActivity(preferencesIntent);
                    return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        private async void VpnListView_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            // Determine the wifi connection status of the device
            if (!ConfirmWifi())
            {
                return;
            }

            using (var progressDialog = new ProgressDialog(this))
            {
                var vpnListView = (ListView)sender;
                var vpnRowItem = vpnListView.GetChildAt(e.Position);
                var vpnSwitch = (Switch)vpnRowItem.FindViewById(Resource.Id.vpnSwitch);

                bool tappedVpnCurrentlyEnabled = vpnSwitch.Checked;

                // Ensure that the correct Vpn Switches are enabled and disabled accordingly
                UpdateSelectedVpnItems(vpnListView, e.Position);

                progressDialog.SetTitle(this.Resources.GetString(Resource.String.UpdatingVpnTitle));
                progressDialog.SetMessage(_updatingMessage);
                progressDialog.Show();

                bool success = false;
                RouterConnectionStatus routerStatus = RouterConnectionStatus.NotConnected;
                try
                {
                    using (var asusCommander = GetAsusSshVpnCommander())
                    {
                        var vpnIndex = e.Position + 1;
                        var connected = await asusCommander.Connect();
                        routerStatus = asusCommander.Connection.Status;

                        switch (routerStatus)
                        {
                            case RouterConnectionStatus.Connected:
                                if (tappedVpnCurrentlyEnabled)
                                {
                                    success = true;
                                    //success = await asusCommander.DisableVpn(vpnIndex);
                                }
                                else
                                {
                                    success = true;
                                    //success = await asusCommander.EnableVpn(vpnIndex);
                                }
                                break;

                            default:
                                DisplayRouterIssueDialog(routerStatus);
                                return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Toast.MakeText(this, String.Format("Unhandled exception {0}", ex.ToString()), ToastLength.Long);
                }
                finally
                {
                    progressDialog.Dismiss();
                }

                if (success)
                {
                    var mainLayout = this.FindViewById<LinearLayout>(Resource.Id.mainLinearLayout);

                    Snackbar.Make(mainLayout, _updatedMessage, Snackbar.LengthLong)
                        .SetAction(this.GetString(Resource.String.OkMessage), (view) => {})
                        .Show();
                }
            }
        }
        #endregion Click Events

        private bool ConfirmWifi()
        {
            DetectAndStoreWifiStatus();

            if (_wifiChecker.Status != WifiStatus.ConnectedToExpectedWifi)
            {
                DisplayWifiIssueDialog();
                return false;
            }

            return true;
        }

        async private Task InitialiseMainScreen()
        {
            bool initialisationIssue = true;

            // Determine the wifi connection status of the device
            if (!ConfirmWifi())
            {
                return;
            }

            // Initialise a list of VpnItems
            var vpnItems = VpnItems();

            var routerStatus = RouterConnectionStatus.NotConnected;

            // Check the current status of vpns
            var progressDialog = new ProgressDialog(this);
            progressDialog.SetTitle(this.GetString(Resource.String.CheckingVpnStatusMessage));
            progressDialog.SetMessage(this.GetString(Resource.String.ConnectingToRouterStatusMessage));
            progressDialog.Show();

            using (var asusCommander = GetAsusSshVpnCommander())
            {
                bool connected = await asusCommander.Connect();

                routerStatus = asusCommander.Connection.Status;

                if (!asusCommander.Connection.IsConnected)
                {
                    DisplayRouterIssueDialog(routerStatus);
                    return;
                }

                progressDialog.SetMessage(this.GetString(Resource.String.CheckingVpnStatusMessage));
                vpnItems = await CheckCurrentVpnStatus(asusCommander, vpnItems);
                initialisationIssue = false;
            }

            progressDialog.Dismiss();

            var adapter = new VpnArrayAdapter(this, vpnItems);
            var vpnListView = FindViewById<ListView>(Resource.Id.vpnListView);
            vpnListView.Adapter = adapter;
            vpnListView.Enabled = !initialisationIssue;
        }

        private async Task<Dictionary<int, VpnItem>> CheckCurrentVpnStatus(AsusSshVpnCommander asusCommander, Dictionary<int, VpnItem> vpnItems)
        {
            if (asusCommander.Connection.IsConnected)
            {
                var vpnStatus = await asusCommander.Status();

                foreach (var key in vpnStatus.Keys)
                {
                    vpnItems[key].Status = vpnStatus[key];
                }
            }

            return vpnItems;
        }

        private Dictionary<int, VpnItem> VpnItems()
        {
            var vpnItems = new Dictionary<int, VpnItem>();

            for (int i = 1; i<this.Resources.GetInteger(Resource.Integer.VpnItemCount) + 1; i++)
            {
                var vpnItemName = string.Format(this.Resources.GetString(Resource.String.VpnClientFormat), i);

                vpnItems.Add(i, new VpnItem(vpnItemName, GetFlagResourceId(_preferences.GetVpnFlag(i)), VpnStatus.Off));
            }

            return vpnItems;
        }

        private AsusSshVpnCommander GetAsusSshVpnCommander()
        {
            var asusCommander = new AsusSshVpnCommander(
                _preferences.RouterIpAddress,
                _preferences.RouterUsername,
                _preferences.RouterPassword,
                int.Parse(_preferences.RouterPort),
                this.Resources.GetInteger(Resource.Integer.ConnectionTimeoutSeconds));

            return asusCommander;
        }

        private int GetFlagResourceId(string flagName)
        {
            int resourceId = Resources.GetIdentifier(flagName.ToLower(), "drawable", PackageName);

            if (resourceId == 0)
            {
                resourceId = Resource.Drawable.DEFAULTFLAG;
            }

            return resourceId;
        }

        private void UpdateSelectedVpnItems(
            ListView vpnListView,
            int selectedIndex)
        {
            var format = string.Empty;

            // Ensure that the correct Vpn Switches are enabled and disabled accordingly
            for (int i = 0; i < vpnListView.Count; i++)
            {
                var vpnRowItem = vpnListView.GetChildAt(i);
                var vpnSwitch = (Switch)vpnRowItem.FindViewById(Resource.Id.vpnSwitch);
                var vpnName = (TextView)vpnRowItem.FindViewById(Resource.Id.vpnName);

                if (i == selectedIndex)
                {
                    vpnSwitch.Checked = !vpnSwitch.Checked;
                    format = vpnSwitch.Checked ? this.Resources.GetString(Resource.String.EnablingVpnFormat) : this.Resources.GetString(Resource.String.DisablingVpnFormat);
                    _updatingMessage = String.Format(format, (string)vpnName.Text);

                    format = vpnSwitch.Checked ? this.Resources.GetString(Resource.String.EnabledVpnFormat) : this.Resources.GetString(Resource.String.DisabledVpnFormat);
                    _updatedMessage = String.Format(format, (string)vpnName.Text);
                }
                else
                {
                    vpnSwitch.Checked = false;
                }
            }
        }

        #region Wifi Utils
        private void DetectAndStoreWifiStatus()
        {
            // Check if we are connected to wifi
            _wifiChecker = new WifiChecker(this.ApplicationContext);
            _wifiChecker.Check(_preferences.WifiName);

            if (_wifiChecker.Connected && _preferences.WifiName == string.Empty)
            {
                // We haven't connected to Wifi before so lets
                // save the name of the Wifi we are connected to 
                // (which we will use to check we are connected to the correct Wifi next time)
                _preferences.WifiName = _wifiChecker.ConnectedToWifiName;
                _preferences.Save();
            }
        }
        #endregion Wifi Utils

        private void DisplayWifiIssueDialog()
        {
            string dialogTitle = this.Resources.GetString(Resource.String.OopsMessage);
            string dialogMessage = String.Empty;

            // Wifi issue?
            if (!_wifiChecker.Connected)
            {
                dialogMessage = this.Resources.GetString(Resource.String.NotConnectedToWifiMessage);
            }
            else if (_wifiChecker.Status != WifiStatus.ConnectedToExpectedWifi)
            {
                dialogMessage = this.Resources.GetString(Resource.String.NotConnectedToExpectedWifiMessage);
            }

            // Display dialog to notify user they're connected to a different wifi connection than last time
            // Allow them to use this one by default, or allow them to quit
            var builder = new AlertDialog.Builder(this)
            .SetTitle(dialogTitle)
            .SetMessage(dialogMessage)
            .SetPositiveButton("Settings", delegate
            {
                _preferences.WifiName = string.Empty;
                _preferences.Save();

                var preferencesIntent = new Intent(Application.Context, typeof(AppPreferencesActivity));
                StartActivity(preferencesIntent);
            })
            .SetNegativeButton("Quit", delegate
            {
                this.Finish();
            })
            .SetCancelable(true)
            .Show();
        }

        private void DisplayRouterIssueDialog(RouterConnectionStatus routerStatus)
        {
            string dialogTitle = this.Resources.GetString(Resource.String.OopsMessage);
            string dialogMessage = String.Empty;

            // Router connection issue?
            if (routerStatus != RouterConnectionStatus.Connected)
            {
                switch (routerStatus)
                {
                    case RouterConnectionStatus.AuthorizationError:
                        dialogMessage = this.Resources.GetString(Resource.String.RouterConnectionAuthorizationIssueMessage);
                        break;
                    case RouterConnectionStatus.NetworkError:
                    case RouterConnectionStatus.ConnectionTimeoutError:
                        dialogMessage = this.Resources.GetString(Resource.String.NetworkIssueMessage);
                        break;
                    default:
                        dialogMessage = this.Resources.GetString(Resource.String.RouterNotConnectedMessage);
                        break;
                }

                var builder = new AlertDialog.Builder(this)
                .SetTitle(dialogTitle)
                .SetMessage(dialogMessage)
                .SetPositiveButton("Settings", delegate
                {
                    var preferencesIntent = new Intent(Application.Context, typeof(AppPreferencesActivity));
                    StartActivity(preferencesIntent);
                })
                .SetNegativeButton("Quit", delegate
                {
                    this.Finish();
                })
                .SetCancelable(true)
                .Show();
            }
        }
    }
}

