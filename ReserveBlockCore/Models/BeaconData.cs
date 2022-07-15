﻿using LiteDB;
using ReserveBlockCore.Data;
using ReserveBlockCore.Utilities;

namespace ReserveBlockCore.Models
{
    public class BeaconData
    {
        public int Id { get; set; }
        public string SmartContractUID { get; set; }
        public string AssetName { get; set; }
        public string AssetSize { get; set; }
        public long AssetReceiveDate { get; set; }
        public long AssetExpireDate { get; set; }
        public string TxHash { get; set; }
        public string SignatureMessage { get; set; }
        public string ToAddress { get; set; }
        public string FromAddress { get; set; }

        public static ILiteCollection<BeaconData>? GetBeacon()
        {
            try
            {
                var beacon = DbContext.DB_Beacon.GetCollection<BeaconData>(DbContext.RSRV_BEACON_DATA);
                return beacon;
            }
            catch (Exception ex)
            {
                ErrorLogUtility.LogError(ex.Message, "BeaconData.GetBeacon()");
                return null;
            }

        }

        public static List<BeaconData>? GetBeaconData()
        {
            try
            {
                var beacon = GetBeacon();

                var beaconInfo = beacon.FindAll().ToList();
                if (beaconInfo.Count() == 0)
                {
                    return null;
                }
                return beaconInfo;
            }
            catch (Exception ex)
            {
                ErrorLogUtility.LogError(ex.Message, "BeaconData.GetBeaconData()");
                return null;
            }

        }

        public static string SaveBeaconData(BeaconData beaconData)
        {
            var beacon = GetBeacon();
            if (beacon == null)
            {
                ErrorLogUtility.LogError("GetBeacon() returned a null value.", "BeaconData.SaveBeaconInfo()");
            }
            else
            {
                var beaconDataRec = beacon.FindOne(x => x.AssetName == beaconData.AssetName);
                if(beaconDataRec != null)
                {
                    return "Record Already Exist";
                }
                else
                {
                    beacon.Insert(beaconData);
                }
            }

            return "Error Saving Beacon Data";

        }

        public static void DeleteAssets(string txHash)
        {
            var beacon = GetBeacon();
            if (beacon == null)
            {
                ErrorLogUtility.LogError("GetBeacon() returned a null value.", "BeaconData.SaveBeaconInfo()");
            }
            else
            {
                beacon.DeleteMany(x => x.TxHash == txHash);
            }
        }


    }
}
