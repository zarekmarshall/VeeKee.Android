using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System.Collections.Generic;
using VeeKee.Shared.Ssh;

namespace VeeKee.Android.Adapters
{
    public class VpnItem
    {
        public string Name { get; set; }
        public int FlagResourceId { get; set; } 
        public VpnStatus Status { get; set; }
        
        public VpnItem(string name, int flagResourceId, VpnStatus status)
        {
            this.Name = name;
            this.FlagResourceId = flagResourceId;
            this.Status = status;
        }
    }
    public class VpnArrayAdapter : BaseAdapter<VpnItem>
    {
        private Dictionary<int, VpnItem> _vpnItems;

        public bool Enabled { get; set; } = false;

        Context _context;

        public VpnArrayAdapter(Context context, Dictionary<int, VpnItem> vpnItems)
        {
            this._context = context;
            this._vpnItems = vpnItems;

        }

        public override Java.Lang.Object GetItem(int position)
        {
            return position;
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            var row = convertView;
            VpnArrayAdapterViewHolder holder = null;

            if (row != null)
            {
                holder = row.Tag as VpnArrayAdapterViewHolder;
            }

            if (holder == null)
            {
                var inflater = _context.GetSystemService(Context.LayoutInflaterService).JavaCast<LayoutInflater>();
                row = inflater.Inflate(Resource.Layout.VpnRow, parent, false);

                // Get references to the row controls
                holder = new VpnArrayAdapterViewHolder();
                holder.VpnSwitch = row.FindViewById<Switch>(Resource.Id.vpnSwitch);
                holder.VpnName = row.FindViewById<TextView>(Resource.Id.vpnName);

                row.Tag = holder;
            }

            // Set the control properties for this row
            var vpnItem = this[position];
            holder.VpnSwitch.Checked = vpnItem.Status == VpnStatus.Enabled;
            holder.VpnName.Text = vpnItem.Name;

            // Disable the row if the status is currently changing
            row.Enabled = vpnItem.Status != VpnStatus.Enabling;

            var flag = this._context.GetDrawable(vpnItem.FlagResourceId);
            flag.SetBounds(0, 0, 157, 105);
            holder.VpnName.SetCompoundDrawables(flag, null, null, null);

            return row;
        }

        public override int Count
        {
            get
            {
                return _vpnItems.Count;
            }
        }

        public override VpnItem this[int position]
        {
            get
            {
                return _vpnItems[position+1];
            }
        }

        public override bool IsEnabled(int position)
        {
            return Enabled;
        }
    }

    public class VpnArrayAdapterViewHolder : Java.Lang.Object
    {
        public Switch VpnSwitch { get; set; }
        public TextView VpnName { get; set; }
    }
}