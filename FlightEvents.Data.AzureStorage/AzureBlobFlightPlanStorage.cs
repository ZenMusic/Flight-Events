﻿using Azure.Storage.Blobs;
using FlightEvents.Common;
using Microsoft.Extensions.Configuration;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace FlightEvents.Data
{
    public class AzureBlobFlightPlanStorage : IFlightPlanStorage
    {
        private readonly BlobContainerClient containerClient;
        private readonly string customDomain;
        private readonly XmlSerializer serializer;

        public AzureBlobFlightPlanStorage(IConfiguration configuration)
        {
            var serviceClient = new BlobServiceClient(configuration["FlightPlan:AzureStorage:ConnectionString"]);
            containerClient = serviceClient.GetBlobContainerClient(configuration["FlightPlan:AzureStorage:ContainerName"]);
            customDomain = configuration["FlightPlan:AzureStorage:CustomDomain"];
            serializer = new XmlSerializer(typeof(FlightPlanDocumentXml));
        }

        public async Task<string> GetFlightPlanUrlAsync(string id)
        {
            await containerClient.CreateIfNotExistsAsync();
            var blobClient = containerClient.GetBlobClient(id);
            if (string.IsNullOrEmpty(customDomain))
            {
                return blobClient.Uri.ToString();
            }
            else
            {
                return blobClient.Uri.ToString().Replace(blobClient.Uri.Host, customDomain);
            }
        }

        public async Task<FlightPlanData> GetFlightPlanAsync(string id)
        {

            await containerClient.CreateIfNotExistsAsync();
            var blobClient = containerClient.GetBlobClient(id);
            var info = await blobClient.DownloadAsync();
            using (info.Value.Content)
            {
                var document = serializer.Deserialize(info.Value.Content) as FlightPlanDocumentXml;
                return document.FlightPlan.ToData();
            }
        }
    }

    [Serializable]
    [DesignerCategory("code")]
    [XmlType(AnonymousType = true)]
    [XmlRoot("SimBase.Document", Namespace = "", IsNullable = false)]
    public partial class FlightPlanDocumentXml
    {
        public string Descr { get; set; }
        [XmlElement("FlightPlan.FlightPlan")]
        public FlightPlanDataXml FlightPlan { get; set; }
        [XmlAttribute]
        public string Type { get; set; }
        [XmlAttribute]
        public string version { get; set; }
    }

    [Serializable]
    [DesignerCategory("code")]
    [XmlType(AnonymousType = true)]
    public partial class FlightPlanDataXml
    {
        public FlightPlanData ToData()
        {
            var data = new FlightPlanData
            {
                Title = Title,
                Type = FPType,
                RouteType = RouteType,
                CruisingAltitude = CruisingAlt,
                Description = Descr,
                Departure = new FlightPlanPosition
                {
                    ID = DepartureID,
                    Name = DepartureName
                },
                Destination = new FlightPlanPosition
                {
                    ID = DestinationID,
                    Name = DestinationName
                },
                Waypoints = ATCWaypoint?.Select(o => o.ToData())
            };
            (data.Departure.Latitude, data.Departure.Longitude) = GpsHelper.ConvertString(DepartureLLA);
            (data.Destination.Latitude, data.Destination.Longitude) = GpsHelper.ConvertString(DestinationLLA);
            return data;
        }

        public string Title { get; set; }
        public string FPType { get; set; }
        public string RouteType { get; set; }
        public int CruisingAlt { get; set; }
        public string DepartureID { get; set; }
        public string DepartureLLA { get; set; }
        public string DestinationID { get; set; }
        public string DestinationLLA { get; set; }
        public string Descr { get; set; }
        public string DepartureName { get; set; }
        public string DestinationName { get; set; }
        public FlightPlanAppVersionXml AppVersion { get; set; }
        [XmlElement("ATCWaypoint")]
        public FlightPlanATCWaypointXml[] ATCWaypoint { get; set; }
    }

    [Serializable]
    [DesignerCategory("code")]
    [XmlType(AnonymousType = true)]
    public partial class FlightPlanAppVersionXml
    {
        public byte AppVersionMajor { get; set; }
        public int AppVersionBuild { get; set; }
    }

    [Serializable]
    [DesignerCategory("code")]
    [XmlType(AnonymousType = true)]
    public partial class FlightPlanATCWaypointXml
    {
        public FlightPlanWaypoint ToData()
        {
            var waypoint = new FlightPlanWaypoint
            {
                Id = id,
                Airway = ATCAirway,
                Type = ATCWaypointType,
                ICAO = ICAO?.ToData()
            };
            (waypoint.Latitude, waypoint.Longitude) = GpsHelper.ConvertString(WorldPosition);
            return waypoint;
        }

        public string ATCWaypointType { get; set; }
        public string WorldPosition { get; set; }
        public string ATCAirway { get; set; }
        public FlightPlanATCWaypointICAOXml ICAO { get; set; }
        [XmlAttribute]
        public string id { get; set; }
    }

    [Serializable]
    [DesignerCategory("code")]
    [XmlType(AnonymousType = true)]
    public partial class FlightPlanATCWaypointICAOXml
    {
        public WaypointICAO ToData() =>
            new WaypointICAO
            {
                Region = ICAORegion,
                Ident = ICAOIdent,
                Airport = ICAOAirport
            };

        public string ICAORegion { get; set; }
        public string ICAOIdent { get; set; }
        public string ICAOAirport { get; set; }
    }
}
