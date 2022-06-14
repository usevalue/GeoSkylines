﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ColossalFramework.UI;
using ICities;
using UnityEngine;
using System.Collections;
using ColossalFramework.Math;
using ColossalFramework;
using System.Text.RegularExpressions;
using burningmime.curves;
using UnityStandardAssets.ImageEffects;
//using System.Drawing;

namespace GeoSkylines
{
    public class GeoSkylinesImport
    {
        private Randomizer rand;
        private System.Random sysRand = new System.Random();
        private Dictionary<short, List<GeoSkylinesNode>> nodeMap = new Dictionary<short, List<GeoSkylinesNode>>();
        private Dictionary<string, List<uint>> zoneBlockMap = new Dictionary<string, List<uint>>();        

        private BuildingManager buildingManager = BuildingManager.instance;
        private NetManager netManager = NetManager.instance;        
        private TerrainManager tm = TerrainManager.instance;
        private SimulationManager sm = SimulationManager.instance;
        private TreeInfo ti;
        private ZoneManager zm = ZoneManager.instance;

        private string impRoadsFileName = "roads_rwo.csv";        
        private string roadMappingFileName = "rwo_cs_road_match.csv";
        private string impRailsFileName = "rails_rwo.csv";
        private string railMappingFileName = "rwo_cs_rail_match.csv";
        private string impBuildingsFileName = "buildings_rwo.csv";
        /private string buildingMatchFileName = "rwo_cs_building_match.csv";
        private string impZonesFileName = "zones_rwo.csv";
        private string zoneMappingFileName = "rwo_cs_zone_match.csv";
        private string impTreesRasterFileName = "trees.png";
        private string impTreesVectorFileName = "trees_rwo.csv";
        private string impWaterWayFileName = "waterway_rwo.csv";
        private string impWaterFileName = "water_rwo.csv";
        private string impServicesFileName = "amenity_rwo.csv";
        private string serviceMatchFileName = "rwo_cs_service_match.csv";

        // set max coords to avoid building objects out of the game area 
        // out of the box game area is 5x5 tiles where 1 tile is 1920 meters (absolute game area is 9x9)
        private ushort coordMax = 8640;

        private WGS84_UTM convertor = new WGS84_UTM(null);
        private UTMResult centerUTM;
        private string mapName;
        private double centerLat;
        private double centerLon;
        private ushort latitudePos = 1;
        private ushort longitudePos = 0;
        private ushort impRoadsCoordMax;
        private ushort impRailsCoordMax;        
        private ushort impBuildingsCoordMax;
        private ushort impZonesCoordMax;
        private ushort impTreesRasterOffTolerance;
        private int density = 1;
        private string[] impTreesTreeTypes;
        private TreeInfo[] treeTypes;
        private ushort impTreesRasterOffsetX;
        private ushort impTreesRasterOffsetY;
        private int impTreesRasterMultiply;
        private ushort impTreesCoordMax;
        private string[] impWaterWayTypes;
        private string[] impWaterWayDepths;
        private string[] impWaterWayWidths;
        private ushort impWaterDepth;

        private ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
        private bool confloaded;

        private Regex CSVParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

        private List<int> heightIdxs = new List<int>();

        public GeoSkylinesImport()
        {
            LoadConfiguration();

            centerUTM = convertor.convertLatLngToUtm(centerLat, centerLon);

        }

        public void LoadConfiguration()
        {
            if (!File.Exists("Files/import_export.txt"))
            {
                panel.SetMessage("GeoSkylines", "No configuration file provided!", false);
                confloaded = false;
            }

            StreamReader confSr = File.OpenText("Files/import_export.txt");
            Dictionary<string, string> conf = new Dictionary<string, string>();
            while (!confSr.EndOfStream)
            {
                string[] keyVal = confSr.ReadLine().Split(':');
                if (keyVal.Length == 2)
                    conf.Add(keyVal[0], keyVal[1]);
            }
            confSr.Close();

            foreach (KeyValuePair<string, string> a_conf in conf)
            {
                var val = a_conf.Value;
                var key = a_conf.Key;

                if (key == "MapName")
                    mapName = val;
                else if (key == "CenterLatitude")
                    double.TryParse(val, out centerLat);
                else if (key == "CenterLongitude")
                    double.TryParse(val, out centerLon);
                else if (key == "ImportTreesRasterOffTolerance")
                    ushort.TryParse(val, out impTreesRasterOffTolerance);
                else if (key == "ImportTreesTreeTypes")
                {
                    Dictionary<string, uint> prefab_name_number = new Dictionary<string, uint>();
                    var prefabCnt = PrefabCollection<TreeInfo>.LoadedCount();
                    for (uint i = 0; i < prefabCnt; i++)
                    {
                        var prefab = PrefabCollection<TreeInfo>.GetPrefab(i);
                        prefab_name_number[prefab.name] = i;
                    }

                    impTreesTreeTypes = val.Split(',').Select(p => p.Trim()).ToArray();
                    List<TreeInfo> l_treeTypes = new List<TreeInfo>();

                    foreach (var prefName in impTreesTreeTypes)
                    {
                        if (prefab_name_number.ContainsKey(prefName))
                            l_treeTypes.Add(PrefabCollection<TreeInfo>.GetLoaded(prefab_name_number[prefName]));
                    }

                    if (l_treeTypes.Count == 0)
                    {
                        l_treeTypes.Add(PrefabCollection<TreeInfo>.GetLoaded(0));
                    }

                    treeTypes = l_treeTypes.ToArray();
                }
                else if (key == "ImportTreesRasterOffsetX")
                    ushort.TryParse(val, out impTreesRasterOffsetX);
                else if (key == "ImportTreesRasterOffsetY")
                    ushort.TryParse(val, out impTreesRasterOffsetY);
                else if (key == "ImportTreesRasterMultiply")
                    int.TryParse(val, out impTreesRasterMultiply);
                else if (key == "ImportWaterWayTypes")
                    impWaterWayTypes = val.Split(',').Select(p => p.Trim()).ToArray();
                else if (key == "ImportWaterWayDepths")
                    impWaterWayDepths = val.Split(',');
                else if (key == "ImportWaterWayWidths")
                    impWaterWayWidths = val.Split(',');
                else if (key == "ImportWaterDepth")
                    ushort.TryParse(val, out impWaterDepth);
                else if (key == "ImportRoadsCoordMax")
                {
                    ushort.TryParse(val, out impRoadsCoordMax);
                    impRoadsCoordMax = Math.Max(Math.Min(impRoadsCoordMax, coordMax), (ushort)960);
                }
                else if (key == "ImportRailsCoordMax")
                {
                    ushort.TryParse(val, out impRailsCoordMax);
                    impRailsCoordMax = Math.Max(Math.Min(impRailsCoordMax, coordMax), (ushort)960);
                }
                else if (key == "ImportBuildingsCoordMax")
                {
                    ushort.TryParse(val, out impBuildingsCoordMax);
                    impBuildingsCoordMax = Math.Max(Math.Min(impBuildingsCoordMax, coordMax), (ushort)960);
                }
                else if (key == "ImportZonesCoordMax")
                {
                    ushort.TryParse(val, out impZonesCoordMax);
                    impZonesCoordMax = Math.Max(Math.Min(impZonesCoordMax, coordMax), (ushort)960);
                }
                else if (key == "ImportTreesCoordMax")
                {
                    ushort.TryParse(val, out impTreesCoordMax);
                    impTreesCoordMax = Math.Max(Math.Min(impTreesCoordMax, coordMax), (ushort)960);
                }
            }              

            confloaded = true;
            
        }

        public void ImportRoads()
        {
            if (!confloaded)
                return;

            Dictionary<string, string> roadMapping = new Dictionary<string, string>();
            if (!File.Exists("Files/" + roadMappingFileName))
            {
                panel.SetMessage("GeoSkylines", roadMappingFileName + " file doesn't exist!", false);
                return;
            }
            else
            {
                StreamReader r_map_sr = File.OpenText("Files/" + roadMappingFileName);
                while (!r_map_sr.EndOfStream)
                {
                    var r_map_vec = CSVParser.Split(r_map_sr.ReadLine());
                    roadMapping[r_map_vec[0]] = r_map_vec[1];
                }
            }
            
            if (!File.Exists("Files/" + impRoadsFileName))
            {
                panel.SetMessage("GeoSkylines", impRoadsFileName + " file doesn't exist!", false);
                return;
            }

            StreamReader sr = File.OpenText("Files/" + impRoadsFileName);

            List<GeoSkylinesRoad> roads = new List<GeoSkylinesRoad>();
            NetInfo ni;

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;
                
            ushort cntNoName = 1;
            ushort bridges = 0;
            ushort cntLenDiff = 0;
            while (!sr.EndOfStream)
            {                                
                fields = CSVParser.Split(sr.ReadLine());
                //Debug.Log("field_names.Length: " + field_names.Length + ", fields.Length: " + fields.Length);
                //Debug.Log("ID: " + fields[2]);
                if (field_names.Length != fields.Length)
                {
                    cntLenDiff++;
                    continue;
                }

                string coords = "";
                string oneWay = "";
                int lanes = 1;
                string streetName = "";
                ulong roadId = 0;
                string roadType = "";
                bool bridge = false;
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");
                    //Debug.Log("columnName: " + columnName + ",  columnValue: " + columnValue);                    
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);

                    if (columnName == "one_way")
                    {
                        oneWay = columnValue;
                        if (oneWay.Length == 0)
                            oneWay = "no";
                    }

                    if (columnName == "lanes")
                        if (columnValue.Length > 0)
                        {
                            //Debug.Log(fields[i] + " - " + columnValue);                            
                            if (!int.TryParse(columnValue, out lanes))
                                lanes = 1;
                        }


                    if (columnName == "road_name")
                    {
                        streetName = columnValue;
                        if (streetName.Length == 0)
                        {
                            streetName = "No Name " + cntNoName.ToString();
                            cntNoName++;
                        }
                    }

                    if (columnName == "id")
                        ulong.TryParse(columnValue, out roadId);                       

                    if (columnName == "road_type")
                        roadType = columnValue;

                    if (columnName == "bridge")
                        if (columnValue != "")
                        {
                            bridge = true;
                            bridges++;
                        }

                }

                if (coords == "")
                    continue;

                Debug.Log("roadId: " + roadId);
                // get the NetInfo prefab here, skip records that have no mapping    
                if (!ObtainNetInfo(out ni, roadMapping, roadType, bridge, oneWay, lanes))
                    continue;               

                // get the vertexes from WKT
                List<Vector2> segCoords = new List<Vector2>();
                string[] coords_v = coords.Split(',');

                foreach (var nodeCoords in coords_v)
                {
                    string[] separatingChars = { " " };
                    string[] nodeCoords_v = nodeCoords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                    var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                    var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                    UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                    float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                    float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                    if (Math.Abs(xCoord) < impRoadsCoordMax && Math.Abs(zCoord) < impRoadsCoordMax)
                    {
                        var pos = new Vector2(xCoord, zCoord);
                        if (segCoords.Count > 0)
                        {
                            var prevP = segCoords[segCoords.Count - 1];
                            var len = VectorUtils.LengthXY(pos-prevP);
                            if (len > 88)
                            {
                                var div = (int)len / 90;
                                div++;

                                var xDiff = pos.x - prevP.x;
                                var yDiff = pos.y - prevP.y;
                                float xDiffStep = xDiff / div;
                                float yDiffStep = yDiff / div;
                                float newX = prevP.x;                                
                                float newY = prevP.y;
                                for (int i = 0; i < div; i++)
                                {
                                    newX = newX + xDiffStep;
                                    newY = newY + yDiffStep;
                                    var newPos = new Vector2(newX, newY);
                                    segCoords.Add(newPos);
                                }
                            }
                            else
                                segCoords.Add(pos);
                        }
                        else
                            segCoords.Add(pos);

                    }
                }
                //Debug.Log("AAA");
                
                var coordsCnt = segCoords.Count;
                if (coordsCnt == 0)
                    continue; // geometry was out of coordMax

                // find roundabouts and make them into one ways  
                if (coordsCnt > 0 && segCoords[0] == segCoords[coordsCnt - 1])
                {                    
                    oneWay = "yes";
                    ObtainNetInfo(out ni, roadMapping, roadType, bridge, oneWay, lanes);
                }

                //Debug.Log("BBB");

                // fill in NodeMap and set junctions
                // something wrong here with roadId 4987481 Array index is out of range.
                foreach (var segCoord in segCoords)
                {
                    if (FindNode(out GeoSkylinesNode inNode, segCoord))
                    {
                        inNode.SetJunction();
                        inNode.roadIds.Add(roadId);
                        inNode.roadNames.Add(streetName);
                    }
                    else
                    {
                        GeoSkylinesNode newNode = new GeoSkylinesNode(segCoord, streetName, roadId);
                        short xRound = (short)Math.Round(segCoord.x);
                        if (!nodeMap.ContainsKey(xRound))
                        {
                            nodeMap.Add(xRound, new List<GeoSkylinesNode>());
                        }
                        nodeMap[xRound].Add(newNode);
                    }
                }
                GeoSkylinesRoad road = new GeoSkylinesRoad(roadId, streetName, roadType, oneWay, lanes, bridge, segCoords, ni, 0);
                roads.Add(road);
            }

            Debug.Log("NUMBER OF DIFF FIELD LEN: " + cntLenDiff);

            //Debug.Log("CCC");
            sr.Close();

            // I need to preserve junctions, otherwise using Bezier curves (big reduction in nodes)   
            // I'm also trying to avoid creating too long segments (111 seems to be the game's max segment length before it starts glitching)
            foreach (var road in roads)
            {
                List<Vector2> tmpPoss = new List<Vector2>();
                float totalLen = 0;
                bool firstSeg = true;
                int cnt = 0;
                foreach (var seg in road.segments)
                {
                    cnt++;
                    totalLen += seg.length;                    
                    // would adding another segment be too long?
                    if (totalLen > 104)
                    {                        
                        CreateCSSegments(road, tmpPoss);
                        tmpPoss = new List<Vector2>();
                        totalLen = seg.length;
                        firstSeg = true;
                    }
                    //Debug.Log("DDD");

                    if (firstSeg)
                    {
                        tmpPoss.Add(seg.startNode);
                        firstSeg = false;
                    }
                    
                    tmpPoss.Add(seg.endNode);

                    if (FindNode(out GeoSkylinesNode inNode, seg.endNode) && inNode.junction)
                    {
                        CreateCSSegments(road, tmpPoss);
                        tmpPoss = new List<Vector2>();
                        totalLen = 0;
                        firstSeg = true;
                    }
                    //Debug.Log("EEE");
                }
                CreateCSSegments(road, tmpPoss);
            }

            panel.SetMessage("GeoSkylines", "Roads import complete.", false);
        }

        public bool ObtainNetInfo(out NetInfo ni, Dictionary<string, string> mapping, string netType, bool bridge, string oneWay, int lanes)
        {                            
            ni = PrefabCollection<NetInfo>.FindLoaded("Basic Road");
            string rt = netType;
            if (oneWay == "yes")
                rt += "_oneway";
            string rt_withLanes = rt;
            if (lanes > 1)
                rt_withLanes += "_" + lanes;

            string prefab_name = "";
            if (mapping.ContainsKey(rt_withLanes))
                prefab_name = mapping[rt_withLanes];
            if (prefab_name == "" && mapping.ContainsKey(rt))
                prefab_name = mapping[rt];
            if (prefab_name == "" && mapping.ContainsKey(netType))
                prefab_name = mapping[netType];
            if (prefab_name == "")
                return false;

            if (PrefabCollection<NetInfo>.LoadedExists(prefab_name))
                ni = PrefabCollection<NetInfo>.FindLoaded(prefab_name);

            if (bridge)
            {
                string prefab_name_bridge = prefab_name + " Bridge";
                if (PrefabCollection<NetInfo>.LoadedExists(prefab_name_bridge))
                    ni = PrefabCollection<NetInfo>.FindLoaded(prefab_name_bridge);
            }

            //if (oneWay == "yes" && ni.name == "Basic Road")
            //    ni = PrefabCollection<NetInfo>.FindLoaded("Oneway Road");
            return true;
        }

        public void CreateCSSegments(GeoSkylinesRoad road, List<Vector2> nodes)
        {
            if (nodes.Count < 2)
                return;

            if (nodes.Count == 2)
            {               
                ObtainCSNode(out ushort startNetNodeId, nodes[0], road.netInfo);
                ObtainCSNode(out ushort endNetNodeId, nodes[1], road.netInfo);

                Vector3 endPos = netManager.m_nodes.m_buffer[endNetNodeId].m_position;
                Vector3 startPos = netManager.m_nodes.m_buffer[startNetNodeId].m_position;
                Vector3 startDir = VectorUtils.NormalizeXZ(endPos - startPos);
                Vector3 endDir = -startDir;

                SimulationManager.instance.AddAction(AddRoad(rand, road.netInfo, startNetNodeId, endNetNodeId, startDir, endDir, road.roadName));
                
                //Debug.Log("Straight: " + road.roadName + " - " + road.roadId); 
            }
            else
            {
                // make curves using burningmime.curves     
                //List<Vector2> reduced = CurvePreprocess.RdpReduce(vertexes.ToList(), 2);
                CubicBezier[] curves = CurveFit.Fit(nodes, 8);

                foreach (var curve in curves)
                {
                    ObtainCSNode(out ushort startNetNodeId, curve.p0, road.netInfo);
                    ObtainCSNode(out ushort endNetNodeId, curve.p3, road.netInfo);

                    Vector3 endPos = netManager.m_nodes.m_buffer[endNetNodeId].m_position;
                    Vector3 startPos = netManager.m_nodes.m_buffer[startNetNodeId].m_position;

                    var controlA = new Vector3(curve.p1.x, 0, curve.p1.y);
                    controlA.y = tm.SampleRawHeightSmoothWithWater(controlA, false, 0f);
                    var controlB = new Vector3(curve.p2.x, 0, curve.p2.y);
                    controlB.y = tm.SampleRawHeightSmoothWithWater(controlB, false, 0f);
                    Vector3 startDir = VectorUtils.NormalizeXZ(controlA - startPos);
                    Vector3 endDir = -VectorUtils.NormalizeXZ(endPos - controlB);

                    SimulationManager.instance.AddAction(AddRoad(rand, road.netInfo, startNetNodeId, endNetNodeId, startDir, endDir, road.roadName));

                    //Debug.Log("Curve: " + road.roadName + " - " + road.roadId);
                }
            }
        }

        public void ObtainCSNode(out ushort netNodeId, Vector2 pos, NetInfo ni)
        {
            netNodeId = 0;

            if (FindNode(out GeoSkylinesNode inNode, pos))
            {
                if (inNode.NetNodeId != -1)
                    netNodeId = (ushort)inNode.NetNodeId;
                else
                {
                    var nodePos = new Vector3(inNode.position.x, 0, inNode.position.y); 
                    nodePos.y = tm.SampleRawHeightSmoothWithWater(nodePos, false, 0f);

                    if (netManager.CreateNode(out netNodeId, ref rand, ni, nodePos, sm.m_currentBuildIndex))
                    {
                        sm.m_currentBuildIndex += 1u;
                    }

                    inNode.NetNodeId = netNodeId;
                }
            }
        }

        public bool FindNode(out GeoSkylinesNode node, Vector2 pos)
        {
            short xRound = (short)Math.Round(pos.x);

            if (nodeMap.ContainsKey(xRound))
            {
                foreach (GeoSkylinesNode sn in nodeMap[xRound])
                {
                    // try to connect with near nodes
                    var xDiff = Mathf.Abs(sn.position.x - pos.x);
                    var zDiff = Mathf.Abs(sn.position.y - pos.y);
                    if (xDiff < 0.5)
                    {
                        if (zDiff < 0.5)
                        {
                            node = sn;
                            return true;
                        }
                    }
                    //if (node.nodeCoords[0] == nodeCoords[0])
                    //    if (node.nodeCoords[1] == nodeCoords[1])
                    //    {
                    //        netNodeId = node.nodeId;
                    //        return true;
                    //    }
                }
            }

            node = null;
            return false;
        }

        public void ImportRoadNames()
        {
            if (!confloaded)
                return;

            Debug.Log(DateTime.Now.ToString("h:mm:ss tt"));

            if (!File.Exists("Files/" + impRoadsFileName))
            {
                panel.SetMessage("GeoSkylines", impRoadsFileName + " file doesn't exist!", false);
                return;
            }

            StreamReader sr = File.OpenText("Files/" + impRoadsFileName);

            List<GeoSkylinesRoad> roads = new List<GeoSkylinesRoad>();
            NetInfo ni = PrefabCollection<NetInfo>.FindLoaded("Basic Road");

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;
                        
            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());

                string coords = "";
                string oneWay = "";
                int lanes = 1;
                string streetName = "";
                ulong roadId = 0;
                string roadType = "";
                bool bridge = false;
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");
                    //Debug.Log(columnName + ": " + columnValue);
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);

                    if (columnName == "road_name")
                    {
                        streetName = columnValue;
                    }

                    if (columnName == "id")
                        ulong.TryParse(columnValue, out roadId);

                }                

                if (coords == "")
                    continue;

                if (streetName == "")
                    continue;

                // get the vertexes from WKT
                List<Vector2> segCoords = new List<Vector2>();
                string[] coords_v = coords.Split(',');

                foreach (var nodeCoords in coords_v)
                {
                    string[] separatingChars = { " " };
                    string[] nodeCoords_v = nodeCoords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                    var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                    var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                    UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                    float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                    float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                    if (Math.Abs(xCoord) < impRoadsCoordMax && Math.Abs(zCoord) < impRoadsCoordMax)
                    {
                        var pos = new Vector2(xCoord, zCoord);                        
                        segCoords.Add(pos);
                    }
                }

                GeoSkylinesRoad road = new GeoSkylinesRoad(roadId, streetName, roadType, oneWay, lanes, bridge, segCoords, ni, 5);
                roads.Add(road);
            }

            sr.Close();

            Debug.Log(DateTime.Now.ToString("h:mm:ss tt"));

            NetSegment[] segments = netManager.m_segments.m_buffer;
            
            for (int i = 0; i < segments.Length; i++)
            {
                var a_seg = segments[i];                

                if (a_seg.m_startNode == 0 || a_seg.m_endNode == 0)
                    continue;

                //var startN = nm.m_nodes.m_buffer[a_seg.m_startNode].m_position;
                //Vector2 startPos = new Vector2(startN.x, startN.z);
                //var endN = nm.m_nodes.m_buffer[a_seg.m_endNode].m_position;
                //Vector2 endPos = new Vector2(endN.x, endN.z);

                var segName = netManager.GetSegmentName((ushort)i);

                var midV3 = a_seg.m_middlePosition;
                Vector2 midV2 = new Vector2(midV3.x, midV3.z);
                List<string> foundStreetNames = new List<string>();

                foreach (var road in roads)
                    foreach (var seg in road.segments)
                    {
                        if (IsPointInPolygon(seg.buffer, midV2))
                            foundStreetNames.Add(seg.road.roadName);
                        //if (IsPointInPolygon(seg.buffer, endPos))
                        //    foundStreetNames.Add(seg.road.roadName);
                    }

                if (foundStreetNames.Count == 0)
                {
                    Debug.Log("Nothing found for seg Id: " + i + ", road name: " + segName);
                    continue;
                }
                    
                
                string msg = "SegId "+ i + " - found street names: \n";
                foreach (var name in foundStreetNames)
                    msg += name + ", ";
                msg += "\n";                                 

                var street_name = foundStreetNames[0];
                msg += "Old name: " + segName + " New name: " + street_name + "\n";
                Debug.Log(msg);

                netManager.SetSegmentNameImpl((ushort)i, street_name);
            }

            Debug.Log(DateTime.Now.ToString("h:mm:ss tt"));
            panel.SetMessage("GeoSkylines", "Road names import complete.", false);
        }

        public void DebugRoad()
        {
            NetSegment[] segments = netManager.m_segments.m_buffer;
            string msg = "";
            for (int i = 0; i < segments.Length; i++)
            {
                msg = "";
                var a_seg = segments[i];

                if (a_seg.m_startNode == 0 || a_seg.m_endNode == 0)
                    continue;

                var segName = netManager.GetSegmentName((ushort)i);
                //if (segName == "test")
                //{
                var startN = netManager.m_nodes.m_buffer[a_seg.m_startNode].m_position;
                var endN = netManager.m_nodes.m_buffer[a_seg.m_endNode].m_position;
                //var startD = a_seg.m_startDirection;
                //var endD = a_seg.m_endDirection;
                //var angle = Vector3.Angle(startD, -endD);
                //msg += "StartNode: " + a_seg.m_startNode + "; pos: " + startN.ToString() + "\n";
                //msg += "EndNode: " + a_seg.m_endNode + "; pos: " + endN.ToString() + "\n";
                //msg += "StartDir: " + startD.ToString() + "\n";
                //msg += "EndDir: " + endD.ToString() + "\n";
                //msg += "Angle: " + angle.ToString() + "\n"; 
                //var startNEndN = startN - endN;
                //msg += "StartNode - EndNode: " + startNEndN.ToString() + "\n";
                //var endNStartN = endN - startN;
                //msg += "EndNode - StartNode: " + endNStartN.ToString() + "\n";
                //var startNStartD = startN - startD;
                //msg += "StartNode - StartDir: " + startNStartD.ToString() + "\n";

                //Vector3 a = Vector3.zero;
                //Vector3 b = Vector3.zero;
                //NetSegment.CalculateMiddlePoints(startN, startD, endN, endD, false, false, out a, out b);

                //msg += "ControlA: " + a.ToString() + "\n";
                //var newStartD = VectorUtils.NormalizeXZ(a - startN);
                //msg += "newStartD = VectorUtils.NormalizeXZ(a - startN): " + newStartD.ToString() + "\n";

                //msg += "ControlB: " + b.ToString() + "\n";          
                //var newEndD = VectorUtils.NormalizeXZ(endN - b);
                //msg += "newEndD = VectorUtils.NormalizeXZ(endN - b): " + newEndD.ToString() + "\n";



                //Vector3 startDirection = VectorUtils.NormalizeXZ(endPos - startPos);
                //Vector3 startDir = VectorUtils.NormalizeXZ(controlA - startPos);
                //Vector3 endDir = VectorUtils.NormalizeXZ(endPos - controlB);

                msg += "NetInfo: " + a_seg.Info + "\n";
                msg += "Road Name: " + segName + "\n";
                var len = VectorUtils.LengthXZ(endN - startN);
                msg += "Length: " + len;

                Debug.Log(msg);
                // }
            }

        }

        public void ImportRails()
        {
            if (!confloaded)
                return;

            Dictionary<string, string> railMapping = new Dictionary<string, string>();

            if (!File.Exists("Files/" + railMappingFileName))
            {
                panel.SetMessage("GeoSkylines", railMappingFileName + " file doesn't exist!", false);
                return;
            }
            else
            {
                StreamReader r_map_sr = File.OpenText("Files/" + railMappingFileName);
                while (!r_map_sr.EndOfStream)
                {
                    var r_map_vec = CSVParser.Split(r_map_sr.ReadLine());
                    railMapping[r_map_vec[0]] = r_map_vec[1];
                }
            }
            
            if (!File.Exists("Files/" + impRailsFileName))
            {
                panel.SetMessage("GeoSkylines", impRailsFileName + " file doesn't exist!", false);
                return;
            }

            StreamReader sr = File.OpenText("Files/" + impRailsFileName);

            List<GeoSkylinesRoad> rails = new List<GeoSkylinesRoad>();
            NetInfo ni;

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;
            
            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());

                string coords = "";
                ulong railId = 0;
                string railType = "";
                bool bridge = false;
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");                                        
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);

                    if (columnName == "rail_type")
                        railType = columnValue;

                    if (columnName == "id")
                        ulong.TryParse(columnValue, out railId);                        

                    if (columnName == "bridge")
                        if (columnValue != "")
                            bridge = true;
                }

                if (coords == "")
                    continue;

                // get the NetInfo prefab here, skip records that have no mapping    
                if (!ObtainNetInfo(out ni, railMapping, railType, bridge, "", 0))
                    continue;

                // get the vertexes from WKT
                List<Vector2> segCoords = new List<Vector2>();
                string[] coords_v = coords.Split(',');

                foreach (var nodeCoords in coords_v)
                {
                    string[] separatingChars = { " " };
                    string[] nodeCoords_v = nodeCoords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                    var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                    var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                    UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                    float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                    float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                    if (Math.Abs(xCoord) < impRailsCoordMax && Math.Abs(zCoord) < impRailsCoordMax)
                    {
                        segCoords.Add(new Vector2(xCoord, zCoord));
                    }
                }

                // fill in NodeMap
                foreach (var segCoord in segCoords)
                {
                    if (!FindNode(out GeoSkylinesNode inNode, segCoord))
                    {
                        GeoSkylinesNode newNode = new GeoSkylinesNode(segCoord, "", railId);
                        short xRound = (short)Math.Round(segCoord.x);
                        if (!nodeMap.ContainsKey(xRound))
                        {
                            nodeMap.Add(xRound, new List<GeoSkylinesNode>());
                        }
                        nodeMap[xRound].Add(newNode);
                    }
                }

                GeoSkylinesRoad rail = new GeoSkylinesRoad(railId, "", railType, "", 0, bridge, segCoords, ni, 0);
                rails.Add(rail);
            }

            sr.Close();

            foreach (var rail in rails)
            {
                foreach (var seg in rail.segments)
                {
                    CreateCSSegments(rail, seg.vertexes.ToList());
                }
                
            }

            panel.SetMessage("GeoSkylines", "Rails import complete.", false);
        }

        public void FixSegments()
        {
            RemoveDisconnectedSegments();
            UpdateSegments();
        }

        public void RemoveDisconnectedSegments()
        {
            for (ushort i = 0; i < netManager.m_segments.m_buffer.Length; i++)
            {
                var seg = netManager.m_segments.m_buffer[i];
                if (seg.m_startNode == 0 || seg.m_endNode == 0)
                    continue;
                var startNode = netManager.m_nodes.m_buffer[seg.m_startNode];
                var endNode = netManager.m_nodes.m_buffer[seg.m_endNode];
                if (startNode.CountSegments() == 1 && endNode.CountSegments() == 1)            
                    netManager.ReleaseSegment(i, false);                                    
            }
        }

        public void NodesConnected()
        {
            int cntDisconnectedSegs = 0;
            int cntAllSegs = 0;
            for (ushort i = 0; i < netManager.m_segments.m_buffer.Length; i++)
            {
                string msg = "";
                var seg = netManager.m_segments.m_buffer[i];
                if (seg.m_startNode == 0 || seg.m_endNode == 0)
                    continue;
                cntAllSegs++;
                var startNode = netManager.m_nodes.m_buffer[seg.m_startNode];
                var endNode = netManager.m_nodes.m_buffer[seg.m_endNode];
                if (startNode.CountSegments() == 1 && endNode.CountSegments() == 1)
                {
                    cntDisconnectedSegs++;
                }

                //var startNode = nm.m_nodes.m_buffer[seg.m_startNode];
                //var endNode = nm.m_nodes.m_buffer[seg.m_endNode];   
                //var seg_name = nm.GetSegmentName(i);
                //msg += seg_name + ": " + i;
                //msg += "\n";
                //msg += "StartNode.Countsegments(): " + startNode.CountSegments();
                //msg += "\n";
                //msg += "EndNode.CountSegments(): " + endNode.CountSegments();
                //msg += "\n";

                //Debug.Log(msg);
            }
            Debug.Log("All segs: " + cntAllSegs + " | Disconnected segs: " + cntDisconnectedSegs);
        }

        public void UpdateSegments()
        {
            for (ushort i = 0; i < netManager.m_segments.m_buffer.Length; i++)
            {
                var seg = netManager.m_segments.m_buffer[i];
                if (seg.m_startNode == 0 || seg.m_endNode == 0)
                    continue;
                netManager.UpdateSegment(i);
            }
        }

        public void SwitchToTrafficLights()
        {
            List<ushort> doneNodes = new List<ushort>();
            Debug.Log("flags.TrafficLights: " + NetNode.Flags.TrafficLights + " flags.None: " + NetNode.Flags.None);
            for (ushort i = 0; i < netManager.m_segments.m_buffer.Length; i++)
            {
                var seg = netManager.m_segments.m_buffer[i];
                if (seg.m_startNode == 0 || seg.m_endNode == 0)
                    continue;

                if (seg.Info.name != "Basic Road")
                    continue;

                ushort[] segNodes = { seg.m_startNode, seg.m_endNode };
                foreach (var nodeId in segNodes)
                {
                    if (!doneNodes.Contains(seg.m_startNode))
                    {
                        //if ((nm.m_nodes.m_buffer[nodeId].m_flags & NetNode.Flags.Junction) != NetNode.Flags.None)
                        if (netManager.m_nodes.m_buffer[nodeId].CountSegments() > 2)
                        {
                            netManager.m_nodes.m_buffer[nodeId].m_flags |= NetNode.Flags.TrafficLights;
                        }

                    }
                    doneNodes.Add(nodeId);
                }
            }
        }

        // working but not used (dificult to place buildings properly, also it's against the game's rules - buidling should have zone first)
        public void ImportBuildings()
        {
            if (!confloaded)
                return;            
            
            List<GeoSkylinesBuilding> buildings = new List<GeoSkylinesBuilding>();            
            if (!File.Exists("Files/" + impBuildingsFileName))
            {
                panel.SetMessage("GeoSkylines", impBuildingsFileName+" file doesn't exist!", false);
                return;
            }
            
            StreamReader sr = File.OpenText("Files/"+ impBuildingsFileName);

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;
            
            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());

                string coords = "";
                float angle = 0f;
                float width = 0f;
                float height = 0f;
                ulong bldId = 0;
                string bldType = "";
                int bldLvl = 0;
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");
                    Debug.Log(columnName + ": " + columnValue);
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);

                    else if (columnName == "building_type")
                        bldType = columnValue;

                    else if (columnName == "height")
                        float.TryParse(columnValue, out height);

                    else if (columnName == "levels" && fields[i].Length != 0)
                        int.TryParse(columnValue, out bldLvl);

                    else if (columnName == "angle")
                        float.TryParse(columnValue, out angle);

                    else if (columnName == "width")
                        float.TryParse(columnValue, out width);

                    else if (columnName == "id")
                        ulong.TryParse(columnValue, out bldId);                        
                }

                if (coords == "")
                    continue;

                string[] separatingChars = { " " };
                string[] nodeCoords_v = coords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                if (Math.Abs(xCoord) < impBuildingsCoordMax && Math.Abs(zCoord) < impBuildingsCoordMax)
                {
                    float[] centroidCoords = new float[] { xCoord, zCoord };
                    GeoSkylinesBuilding csBld = new GeoSkylinesBuilding(bldId, bldType, bldLvl, centroidCoords, angle, width, height);
                    buildings.Add(csBld);
                }
            }

            sr.Close();

            BuildingInfo bi;
            
            foreach (var bld in buildings)
            {
                Vector3 bldPos = new Vector3(bld.bldCentroid[0], 0, bld.bldCentroid[1]);
                bldPos.y = tm.SampleRawHeightSmoothWithWater(bldPos, false, 0f);
                string building_type = bld.bldType;
                int bldLvl = bld.bldLvl;
                float angle = bld.angle;

                string[] allowedBuildingTypes = { "yes", "residential", "house" };
                if (!allowedBuildingTypes.Contains(building_type))
                    continue;

                if (building_type == "yes")
                {
                    bi = PrefabCollection<BuildingInfo>.FindLoaded("L1 2x2 Detached03");
                }
                else if (building_type == "residential")
                {
                    bi = PrefabCollection<BuildingInfo>.FindLoaded("L1 2x2 Detached06");
                }
                else if (building_type == "house")
                {
                    bi = PrefabCollection<BuildingInfo>.FindLoaded("L1 1x1 Detached");
                }
                else
                {
                    bi = PrefabCollection<BuildingInfo>.FindLoaded("L1 2x2 Detached06");
                }

                SimulationManager.instance.AddAction(AddBuilding(bi, bldPos, angle));
            }

            panel.SetMessage("GeoSkylines", "Buildings import completed. ", false);
        }

        public void ImportTreesRaster()
        {
            if (!confloaded)
                return;
            
            string rasterFilePath = string.Format("{0}{1}", "Files/", impTreesRasterFileName.Trim());
            if (!File.Exists(rasterFilePath))
            {
                panel.SetMessage("GeoSkylines", rasterFilePath + " file doesn't exist!", false);
                return;
            }

            Texture2D tex = new Texture2D(0,0);
            byte[] bytes = File.ReadAllBytes(rasterFilePath);
            tex.LoadImage(bytes);                       

            var height = tex.height;
            var width = tex.width;
            Debug.Log("height: " + height.ToString() + " width: " + width.ToString());
            if (height < 1081 - impTreesRasterOffTolerance || height > 1081 + impTreesRasterOffTolerance ||
                width < 1081 - impTreesRasterOffTolerance || width > 1081 + impTreesRasterOffTolerance)
            {
                string msg = "The size of provided raster is not within the range ";
                msg += (1081 - impTreesRasterOffTolerance).ToString();
                msg += " - ";
                msg += (1081 + impTreesRasterOffTolerance).ToString();
                panel.SetMessage("GeoSkylines", msg, false);
                return;
            }

            float scale = 17280 / height;            

            int trees_found = 0; 
            
            Color pixel_color;

            int step = 999999999;
            if (impTreesRasterMultiply != 0)
            {
                step = impTreesRasterMultiply;
            }

            string debug_msg = "";
            int treesCreated = 0;
            for (int x = 0; x < height; x += density)
            {
                for (int y = 0; y < width; y += density)
                {  
                    pixel_color = tex.GetPixel(x, y);                                                           

                    // anything other than white
                    if (pixel_color.g < 1) 
                    {
                        trees_found++;

                        if (treesCreated > 250000)
                        {
                            Debug.Log("Tree limit 250000 reached. ");
                            break;
                        }

                        if (trees_found % step == 0)
                        {
                            if (step < 0)
                                continue;
                            else
                            {
                                if (AddTreeFromPixels(x, y, scale))
                                    treesCreated++;
                            }
                        }

                        if (AddTreeFromPixels(x,y,scale))
                            treesCreated++;

                    }
                }
            }

            Debug.Log(debug_msg);
            Debug.Log("Trees found: " + trees_found.ToString());

            panel.SetMessage("GeoSkylines", "Trees (from raster data) import completed. ", false);
        }

        public bool AddTreeFromPixels(int x, int y, float scale)
        {
            var gameX = x * scale - 8640;
            var gameZ = y * scale - 8640;
            double randomizedX = gameX + (0.5 - sysRand.NextDouble()) * scale + impTreesRasterOffsetX;
            double randomizedZ = gameZ + (0.5 - sysRand.NextDouble()) * scale + impTreesRasterOffsetY;

            if (Math.Abs(randomizedX) < impTreesCoordMax && Math.Abs(randomizedZ) < impTreesCoordMax)
            {
                var treePos = new Vector3((float)randomizedX, 0f, (float)randomizedZ);

                ti = treeTypes[sysRand.Next(0, treeTypes.Length)];
                SimulationManager.instance.AddAction(AddTree(treePos, SimulationManager.instance.m_randomizer, ti));

                return true;

            }
            else
                return false;            
        }

        public void ImportTreesVector()
        {
            if (!confloaded)
                return;
            
            if (!File.Exists("Files/"+ impTreesVectorFileName))
            {
                panel.SetMessage("GeoSkylines", impTreesVectorFileName+" file doesn't exist!", false);
                return;
            }

            StreamReader sr = File.OpenText("Files/"+ impTreesVectorFileName);

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;            

            int cnt = 0;
            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());
                
                string coords = "";
                string treeType = "";
                ulong treeId = 0;
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");
                    //Debug.Log(columnName + ": " + columnValue);
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);

                    else if (columnName == "type")
                        treeType = columnValue;

                    else if (columnName == "id")
                        ulong.TryParse(columnValue, out treeId);                        
                }

                if (coords == "")
                    continue;

                string[] separatingChars = { " " };
                string[] nodeCoords_v = coords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);                
                if (Math.Abs(xCoord) < impTreesCoordMax && Math.Abs(zCoord) < impTreesCoordMax)
                {                    
                    if (cnt > 250000)
                    {
                        Debug.Log("Tree limit 250000 reached. "); 
                        break;
                    }

                    ti = treeTypes[sysRand.Next(0, treeTypes.Length)];
                    SimulationManager.instance.AddAction(AddTree(new Vector3(xCoord,0f,zCoord), rand, ti));

                    cnt++;
                }
            }

            sr.Close();

            panel.SetMessage("GeoSkylines", "Trees (from vector data) import completed. ", false);

        }

        public void ImportWaterWay()
        {
            if (!confloaded)
                return;
            
            if (!File.Exists("Files/"+ impWaterWayFileName))
            {
                panel.SetMessage("GeoSkylines", impWaterWayFileName+" file doesn't exist!", false);
                return;
            }

            StreamReader sr = File.OpenText("Files/"+ impWaterWayFileName);

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;            

            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());

                string coords = "";
                string waterWayType = "";
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");                    
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);
                    else if (columnName == "waterway")
                        waterWayType = columnValue;
                }

                Debug.Log(coords);
                Debug.Log(waterWayType);
                if (coords == "")
                    continue;

                if (!impWaterWayTypes.Contains(waterWayType))
                    continue;

                float oneMetreBytes = ushort.MaxValue / (ushort)TerrainManager.TERRAIN_HEIGHT;
                ushort depthMetre;
                ushort.TryParse(impWaterWayDepths[Array.IndexOf(impWaterWayTypes, waterWayType)], out depthMetre);
                ushort depthBytes = (ushort)(depthMetre * oneMetreBytes);

                ushort widthTexel;
                ushort.TryParse(impWaterWayWidths[Array.IndexOf(impWaterWayTypes, waterWayType)], out widthTexel);                

                List<Vector3> waterwayNodes = new List<Vector3>();

                string[] separatingChars = { " " };                
                string[] coords_v = coords.Split(',');
                foreach (var nodeCoords in coords_v)
                {                    
                    string[] nodeCoords_v = nodeCoords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                    var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                    var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                    UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                    float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                    float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                    if (Math.Abs(xCoord) < coordMax && Math.Abs(zCoord) < coordMax)
                    {
                        var pos = new Vector3(xCoord, 0f, zCoord);
                        pos.y = tm.SampleRawHeightSmoothWithWater(pos, false, 0f);
                        waterwayNodes.Add(pos);
                    }
                }

                for (int i = 0; i < waterwayNodes.Count - 1; i++)
                {
                    var startPos = waterwayNodes[i];
                    var endPos = waterwayNodes[i + 1];

                    List<Vector3> positions = new List<Vector3>
                    {
                        startPos,
                        endPos
                    };

                    var diffX = endPos.x - startPos.x;
                    var diffZ = endPos.z - startPos.z;
                    float dist = VectorUtils.LengthXZ(endPos - startPos);                    

                    int step = 5;
                    if (dist > step)
                    {
                        var stepDist = dist / step;
                        var stepX = diffX / dist * step;
                        var stepZ = diffZ / dist * step;
                        var stepPosX = startPos.x;
                        var stepPosZ = startPos.z;
                        Vector3 tmpPos = Vector3.zero;
                        for (int j = 0; j < (int)stepDist; j++)
                        {
                            stepPosX = stepPosX + (float)stepX;
                            stepPosZ = stepPosZ + (float)stepZ;
                            tmpPos.x = stepPosX;
                            tmpPos.z = stepPosZ;                            
                            tmpPos.y = tm.SampleRawHeightSmoothWithWater(tmpPos, false, 0f);
                            positions.Add(tmpPos);
                        }
                    }

                    foreach (var pos in positions)
                    {
                        int pixelX = Mathf.Max((int)((pos.x) / TerrainManager.RAW_CELL_SIZE + (float)TerrainManager.RAW_RESOLUTION * 0.5f), 0);
                        int pixelZ = Mathf.Max((int)((pos.z) / TerrainManager.RAW_CELL_SIZE + (float)TerrainManager.RAW_RESOLUTION * 0.5f), 0);

                        List<int> tmpIdxs = new List<int>();
                        tmpIdxs.Add(pixelZ * (TerrainManager.RAW_RESOLUTION + 1) + pixelX);
                        for (int addedWidth=1; addedWidth < widthTexel+1; addedWidth++)
                        {
                            tmpIdxs.Add(pixelZ * (TerrainManager.RAW_RESOLUTION + 1) + (pixelX + addedWidth));
                            tmpIdxs.Add((pixelZ + addedWidth) * (TerrainManager.RAW_RESOLUTION + 1) + (pixelX + addedWidth));
                            tmpIdxs.Add((pixelZ + addedWidth) * (TerrainManager.RAW_RESOLUTION + 1) + pixelX);
                            tmpIdxs.Add((pixelZ + addedWidth) * (TerrainManager.RAW_RESOLUTION + 1) + (pixelX - addedWidth));
                            tmpIdxs.Add(pixelZ * (TerrainManager.RAW_RESOLUTION + 1) + (pixelX - addedWidth));
                            tmpIdxs.Add((pixelZ - addedWidth) * (TerrainManager.RAW_RESOLUTION + 1) + (pixelX - addedWidth));
                            tmpIdxs.Add((pixelZ - addedWidth) * (TerrainManager.RAW_RESOLUTION + 1) + pixelX);
                            tmpIdxs.Add((pixelZ - addedWidth) * (TerrainManager.RAW_RESOLUTION + 1) + (pixelX + addedWidth));
                        }

                        foreach (var idx in tmpIdxs)
                        {
                            if (heightIdxs.Contains(idx))
                                continue;
                            heightIdxs.Add(idx);
                            var currentHeight = tm.RawHeights[idx];
                            ushort newHeight = (ushort)(currentHeight - depthBytes);
                            tm.RawHeights[idx] = newHeight;                            
                            TerrainModify.UpdateArea(pixelX - 2, pixelZ - 2, pixelX + 2, pixelZ + 2, true, true, false);
                        }                                                                            
                    }
                }
            }

            panel.SetMessage("GeoSkylines", "Diging water ways complete. ", false);
        }

        public void ImportWaterReservoirs()
        { 
            if (!confloaded)
                return;
            
            if (!File.Exists("Files/"+ impWaterFileName))
            {
                panel.SetMessage("GeoSkylines", impWaterFileName+" file doesn't exist!", false);
                return;
            }

            StreamReader sr = File.OpenText("Files/"+ impWaterFileName);

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;            

            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());

                string coords = "";
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");                                        
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);
                }

                if (coords == "")
                    continue;

                float oneMetreBytes = ushort.MaxValue / (ushort)TerrainManager.TERRAIN_HEIGHT;
                ushort depthMetre = 15;
                if (impWaterDepth != 0)
                    depthMetre = impWaterDepth;
                ushort depthBytes = (ushort)(depthMetre * oneMetreBytes);

                List<Vector2> waterbodyNodes = new List<Vector2>();
                float xMin = 8641;
                float xMax = -8641;
                float yMin = 8641;
                float yMax = -8641;

                string[] separatingChars = { " " };
                string[] coords_v = coords.Split(',');
                foreach (var nodeCoords in coords_v)
                {
                    string[] nodeCoords_v = nodeCoords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                    var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                    var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                    UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                    float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                    float yCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                    if (Math.Abs(xCoord) < coordMax && Math.Abs(yCoord) < coordMax)
                    {
                        var pos = new Vector2(xCoord, yCoord);                        
                        waterbodyNodes.Add(pos);
                        xMin = Mathf.Min(xMin, pos.x);
                        xMax = Mathf.Max(xMax, pos.x);
                        yMin = Mathf.Min(yMin, pos.y);
                        yMax = Mathf.Max(yMax, pos.y);
                    }
                }

                Vector2[] polygonVertices = waterbodyNodes.ToArray();

                int xMinInt = (int)xMin;
                int xMaxInt = (int)xMax + 1;
                int yMinInt = (int)yMin;
                int yMaxInt = (int)yMax + 1;
                
                for (int i = 0; i < (xMaxInt - xMinInt); i+=5)
                    for (int j = 0; j < (yMaxInt - yMinInt); j+=5)
                    {
                        Vector2 pos = new Vector2(xMin + i, yMin + j);
                        if (IsPointInPolygon(polygonVertices, pos))
                        {
                            int pixelX = Mathf.Max((int)((pos.x) / TerrainManager.RAW_CELL_SIZE + (float)TerrainManager.RAW_RESOLUTION * 0.5f), 0);
                            int pixelZ = Mathf.Max((int)((pos.y) / TerrainManager.RAW_CELL_SIZE + (float)TerrainManager.RAW_RESOLUTION * 0.5f), 0);

                            int idx = pixelZ * (TerrainManager.RAW_RESOLUTION + 1) + pixelX;

                            if (heightIdxs.Contains(idx))
                                continue;
                            heightIdxs.Add(idx);
                            var currentHeight = TerrainManager.instance.RawHeights[idx];
                            ushort newHeight = (ushort)(currentHeight - depthBytes);
                            TerrainManager.instance.RawHeights[idx] = newHeight;                            
                        }
                    }

                int xMinArea = Mathf.Max((int)((xMinInt) / TerrainManager.RAW_CELL_SIZE + (float)TerrainManager.RAW_RESOLUTION * 0.5f), 0);
                int xMaxArea = Mathf.Min((int)((xMaxInt) / TerrainManager.RAW_CELL_SIZE + (float)TerrainManager.RAW_RESOLUTION * 0.5f), TerrainManager.RAW_RESOLUTION);
                int yMinArea = Mathf.Max((int)((yMinInt) / TerrainManager.RAW_CELL_SIZE + (float)TerrainManager.RAW_RESOLUTION * 0.5f), 0);
                int yMaxArea = Mathf.Min((int)((yMaxInt) / TerrainManager.RAW_CELL_SIZE + (float)TerrainManager.RAW_RESOLUTION * 0.5f), TerrainManager.RAW_RESOLUTION);

                TerrainModify.UpdateArea(xMinArea-1, yMinArea-1, xMaxArea+1, yMaxArea+1, true, true, false);
            }

            panel.SetMessage("GeoSkylines", "Diging water reservoirs complete. ", false);
        }

        private bool IsPointInPolygon(Vector2[] polygon, Vector2 point)
        {
            bool isInside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    isInside = !isInside;
                }
            }
            return isInside;
        }

        public void ImportServices()
        {
            if (!confloaded)
                return;            

            if (!File.Exists("Files/" + impServicesFileName))
            {
                panel.SetMessage("GeoSkylines", impServicesFileName + " file doesn't exist!", false);
                return;
            }

            Dictionary<string, uint> bldPrefab_name_number = new Dictionary<string, uint>();
            var prefabCnt = PrefabCollection<BuildingInfo>.LoadedCount();
            for (uint i = 0; i < prefabCnt; i++)
            {
                var prefab = PrefabCollection<BuildingInfo>.GetPrefab(i);
                bldPrefab_name_number[prefab.name] = i;
            }
            string msg = "";
            foreach (var pfn in bldPrefab_name_number)
            {
                msg += pfn + "\n";
            }
            Debug.Log(msg);

            Dictionary<string, uint> serviceMapping = new Dictionary<string, uint>();            
            if (!File.Exists("Files/" + serviceMatchFileName))
            {
                panel.SetMessage("GeoSkylines", serviceMatchFileName + " file doesn't exist!", false);
                return;
            }
            else
            {
                StreamReader s_map_sr = File.OpenText("Files/" + serviceMatchFileName);
                while (!s_map_sr.EndOfStream)
                {
                    var s_map_vec = CSVParser.Split(s_map_sr.ReadLine());
                    Debug.Log(s_map_vec[0] + ": " + s_map_vec[1]); 
                    if (bldPrefab_name_number.ContainsKey(s_map_vec[1]))
                        serviceMapping[s_map_vec[0]] = bldPrefab_name_number[s_map_vec[1]];
                }
            }

            List<GeoSkylinesService> services = new List<GeoSkylinesService>();
            StreamReader sr = File.OpenText("Files/" + impServicesFileName);

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;
            
            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());

                string coords = "";
                ulong serviceId = 0;
                string serviceType = "";
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");
                    //Debug.Log(columnName + ": " + columnValue);
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);

                    else if (columnName == "amenity")
                        serviceType = columnValue;

                    else if (columnName == "id")
                        ulong.TryParse(columnValue, out serviceId);                        
                }

                if (coords == "")
                    continue;

                string[] separatingChars = { " " };
                string[] nodeCoords_v = coords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                if (Math.Abs(xCoord) < impBuildingsCoordMax && Math.Abs(zCoord) < impBuildingsCoordMax)
                {
                    float[] centroidCoords = new float[] { xCoord, zCoord };
                    GeoSkylinesService csService = new GeoSkylinesService(serviceId, serviceType, centroidCoords);
                    services.Add(csService);
                }
            }

            sr.Close();

            BuildingInfo bi;

            foreach (var a_service in services)
            {
                Vector3 servicePos = new Vector3(a_service.serviceCentroid[0], 0, a_service.serviceCentroid[1]);
                servicePos.y = tm.SampleRawHeightSmoothWithWater(servicePos, false, 0f);
                string service_type = a_service.serviceType;

                bi = PrefabCollection<BuildingInfo>.FindLoaded("MerryGoRound");
                uint prefab_id = 0;
                if (serviceMapping.ContainsKey(service_type))
                    prefab_id = serviceMapping[service_type];
                if (prefab_id == 0)
                    continue;

                bi = PrefabCollection<BuildingInfo>.GetLoaded(prefab_id);

                //float angle = Mathf.Atan2(servicePos.x, -servicePos.z);
                float angle = 0f;

                SimulationManager.instance.AddAction(AddBuilding(bi, servicePos, angle));
            }

            panel.SetMessage("GeoSkylines", "Services import completed. ", false);
        }

        // this was an attempt to set zones from buildings but it's not so useful if 95% of buildings are of one type
        public void ImportZones()
        {
            if (!confloaded)
                return;

            List<GeoSkylinesBuilding> buildings = new List<GeoSkylinesBuilding>();

            if (!File.Exists("Files/" + impBuildingsFileName))
            {
                panel.SetMessage("GeoSkylines", impBuildingsFileName + " file doesn't exist!", false);
                return;
            }

            Dictionary<string, string> zoneMapping = new Dictionary<string, string>();              
            if (!File.Exists("Files/" + zoneMappingFileName))
            {
                panel.SetMessage("GeoSkylines", zoneMappingFileName + " file doesn't exist!", false);
                return;
            }
            else
            {
                StreamReader z_map_sr = File.OpenText("Files/" + zoneMappingFileName);
                while (!z_map_sr.EndOfStream)
                {
                    var z_map_vec = CSVParser.Split(z_map_sr.ReadLine());
                    zoneMapping[z_map_vec[0]] = z_map_vec[1];
                }
            }

            var zoneBlocks = zm.m_blocks.m_buffer;
            for (ushort i = 0; i < zoneBlocks.Length; i++)
            {
                //var zoneBlock = zoneBlocks[i];
                var pos = zoneBlocks[i].m_position;
                if (pos != Vector3.zero)
                {
                    int zoneMapX = (int)pos.x / 120;
                    int zoneMapZ = (int)pos.z / 120;
                    string zoneMapXZ = zoneMapX + "," + zoneMapZ;
                    
                    if (!zoneBlockMap.ContainsKey(zoneMapXZ))
                    {
                        zoneBlockMap.Add(zoneMapXZ, new List<uint>());
                    }
                    zoneBlockMap[zoneMapXZ].Add(i);
                }
            }

            string msg = "";
            int tmpCnt = 0;
            foreach (KeyValuePair<string, List<uint>> an_el in zoneBlockMap)
            {
                tmpCnt++;
                if (tmpCnt > 5)
                    continue;

                msg += an_el.Key + ": \n";
                foreach (var blockId in an_el.Value)
                    msg += blockId + "\n";
                msg += "***************\n";
            }
            //Debug.Log(msg);
            //Debug.Log("impBuildingsCoordMax: " + impBuildingsCoordMax);

            StreamReader sr = File.OpenText("Files/" + impBuildingsFileName);

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;
            
            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());

                string coords = "";
                float angle = 0f;
                float width = 0f;
                float height = 0f;
                ulong bldId = 0;
                string bldType = "";
                int bldLvl = 0;
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");                    
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);

                    else if (columnName == "building_type")
                        bldType = columnValue;

                    else if (columnName == "height")
                        float.TryParse(columnValue, out height);

                    else if (columnName == "levels" && fields[i].Length != 0)
                        int.TryParse(columnValue, out bldLvl);

                    else if (columnName == "angle")
                        float.TryParse(columnValue, out angle);

                    else if (columnName == "width")
                        float.TryParse(columnValue, out width);

                    else if (columnName == "id")
                        ulong.TryParse(columnValue, out bldId);
                }

                if (coords == "")
                    continue;

                string[] separatingChars = { " " };
                string[] nodeCoords_v = coords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);

                var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                if (Math.Abs(xCoord) < impBuildingsCoordMax && Math.Abs(zCoord) < impBuildingsCoordMax)
                {
                    float[] centroidCoords = new float[] { xCoord, zCoord };
                    GeoSkylinesBuilding csBld = new GeoSkylinesBuilding(bldId, bldType, bldLvl, centroidCoords, angle, width, height);
                    buildings.Add(csBld);
                }
            }

            //Debug.Log("Buildings amount: " + buildings.Count);

            sr.Close();

            foreach (var bld in buildings)
            {
                Vector3 bldPos = new Vector3(bld.bldCentroid[0], 0, bld.bldCentroid[1]);
                bldPos.y = tm.SampleRawHeightSmoothWithWater(bldPos, false, 0f);
                string building_type = bld.bldType;
                int bldLvl = bld.bldLvl;
                float angle = bld.angle;
                //ItemClass.Zone.CommercialHigh; //5
                //ItemClass.Zone.CommercialLow; //4
                //ItemClass.Zone.Distant; //1
                //ItemClass.Zone.Industrial; //6
                //ItemClass.Zone.None; //15
                //ItemClass.Zone.Office; //7
                //ItemClass.Zone.ResidentialHigh; //3
                //ItemClass.Zone.ResidentialLow; //2
                //ItemClass.Zone.Unzoned; //0

                //Debug.Log(building_type);
                string zone_name = "";
                if (zoneMapping.ContainsKey(building_type))
                    zone_name = zoneMapping[building_type];
                if (zone_name == "")
                    continue;

                //Debug.Log("After zoneMapping!");

                int zoneMapX = (int)bldPos.x / 120;
                int zoneMapZ = (int)bldPos.z / 120;
                string zoneMapXZ = zoneMapX + "," + zoneMapZ;
                if (!zoneBlockMap.ContainsKey(zoneMapXZ))
                {
                    Debug.Log("Not found in zoneBlockMap: " + zoneMapXZ);
                    continue;
                }

                ItemClass.Zone zone = ItemClass.Zone.None;
                switch (zone_name)
                {
                    case "CommercialHigh":
                        zone = ItemClass.Zone.CommercialHigh;
                        break;
                    case "CommercialLow":
                        zone = ItemClass.Zone.CommercialLow;
                        break;
                    case "Industrial":
                        zone = ItemClass.Zone.Industrial;
                        break;
                    case "Office":
                        zone = ItemClass.Zone.Office;
                        break;
                    case "ResidentialHigh":
                        zone = ItemClass.Zone.ResidentialHigh;
                        break;
                    case "ResidentialLow":
                        zone = ItemClass.Zone.ResidentialLow;
                        break;
                }

                foreach (var zoneBlockId in zoneBlockMap[zoneMapXZ])
                {
                    int num = (int)((zoneBlocks[zoneBlockId].m_flags & 65280u) >> 8);
                    var blockPos = zoneBlocks[zoneBlockId].m_position;
                    if ((blockPos.x < bldPos.x+5 && blockPos.x > bldPos.x-5) && 
                        (blockPos.z < bldPos.z+5 && blockPos.z > bldPos.z-5))
                    {
                        Debug.Log("XXXXXXXXXXX");
                        for (int z = 0; z < num; z++)
                            for (int x = 0; x < 4; x++)
                            {
                                if (zoneBlocks[zoneBlockId].SetZone(x, z, zone))
                                    Debug.Log("Zone set to: " + zone);
                            }
                        zoneBlocks[zoneBlockId].RefreshZoning((ushort)zoneBlockId);
                    }
                }

                //string[] allowedBuildingTypes = { "yes", "residential", "house" };
                //if (!allowedBuildingTypes.Contains(building_type))
                //    continue;                

                //SimulationManager.instance.AddAction(AddBuilding(bi, bldPos, angle));
            }

            panel.SetMessage("GeoSkylines", "Zones import completed. ", false);

        }

        public void ImportZonesArea()
        {
            if (!confloaded)
                return;

            List<GeoSkylinesZone> zones = new List<GeoSkylinesZone>();

            if (!File.Exists("Files/" + impZonesFileName))
            {
                panel.SetMessage("GeoSkylines", impZonesFileName + " file doesn't exist!", false);
                return;
            }

            Dictionary<string, string> zoneMapping = new Dictionary<string, string>();
            if (!File.Exists("Files/" + zoneMappingFileName))
            {
                panel.SetMessage("GeoSkylines", zoneMappingFileName + " file doesn't exist!", false);
                return;
            }
            else
            {
                StreamReader z_map_sr = File.OpenText("Files/" + zoneMappingFileName);
                while (!z_map_sr.EndOfStream)
                {
                    var z_map_vec = CSVParser.Split(z_map_sr.ReadLine());
                    zoneMapping[z_map_vec[0]] = z_map_vec[1];
                }
            }

            var zoneBlocks = zm.m_blocks.m_buffer;
            for (ushort i = 0; i < zoneBlocks.Length; i++)
            {
                //var zoneBlock = zoneBlocks[i];
                var pos = zoneBlocks[i].m_position;
                if (pos != Vector3.zero)
                {
                    int zoneMapX = (int)pos.x / 120;
                    int zoneMapZ = (int)pos.z / 120;
                    string zoneMapXZ = zoneMapX + "," + zoneMapZ;

                    if (!zoneBlockMap.ContainsKey(zoneMapXZ))
                    {
                        zoneBlockMap.Add(zoneMapXZ, new List<uint>());
                    }
                    zoneBlockMap[zoneMapXZ].Add(i);
                }
            }

            StreamReader sr = File.OpenText("Files/" + impZonesFileName);

            string[] field_names = CSVParser.Split(sr.ReadLine());
            string[] fields;
            
            while (!sr.EndOfStream)
            {
                fields = CSVParser.Split(sr.ReadLine());

                string coords = "";
                ulong zoneId = 0;
                string zoneType = "";
                for (int i = 0; i < field_names.Length; i++)
                {
                    var columnName = field_names[i].Trim().ToLower();
                    var columnValue = fields[i].Replace("\"", "");                    
                    if (columnName == "geometry")
                        ProvideCoordsString(out coords, columnValue);

                    else if (columnName == "zone_type")
                        zoneType = columnValue;

                    else if (columnName == "id")
                        ulong.TryParse(columnValue, out zoneId);
                }

                if (coords == "")
                    continue;

                //Debug.Log(coords);
                List<Vector2> zoneNodes = new List<Vector2>();

                string[] separatingChars = { " " };
                string[] coords_v = coords.Split(',');
                foreach (var nodeCoords in coords_v)
                {
                    string[] nodeCoords_v = nodeCoords.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);
                    var lat = double.Parse(nodeCoords_v[latitudePos].Trim());
                    var lon = double.Parse(nodeCoords_v[longitudePos].Trim());
                    UTMResult utmCoords = convertor.convertLatLngToUtm(lat, lon);
                    float xCoord = (float)(utmCoords.Easting - centerUTM.Easting);
                    float zCoord = (float)(utmCoords.Northing - centerUTM.Northing);
                    if (Math.Abs(xCoord) < impZonesCoordMax && Math.Abs(zCoord) < impZonesCoordMax)
                    {
                        var pos = new Vector2(xCoord, zCoord);
                        zoneNodes.Add(pos);
                    }
                }
                GeoSkylinesZone csZone = new GeoSkylinesZone(zoneId, zoneType, zoneNodes.ToArray());
                zones.Add(csZone);
            }

            Debug.Log("Zones amount: " + zones.Count);

            sr.Close();

            foreach (var a_zone in zones)
            {
                //ItemClass.Zone.CommercialHigh; //5
                //ItemClass.Zone.CommercialLow; //4
                //ItemClass.Zone.Distant; //1
                //ItemClass.Zone.Industrial; //6
                //ItemClass.Zone.None; //15
                //ItemClass.Zone.Office; //7
                //ItemClass.Zone.ResidentialHigh; //3
                //ItemClass.Zone.ResidentialLow; //2
                //ItemClass.Zone.Unzoned; //0

                //Debug.Log(building_type);
                string zone_name = "";
                if (zoneMapping.ContainsKey(a_zone.zoneType))
                    zone_name = zoneMapping[a_zone.zoneType];
                if (zone_name == "")
                    continue;

                ItemClass.Zone zone = ItemClass.Zone.None;
                switch (zone_name)
                {
                    case "CommercialHigh":
                        zone = ItemClass.Zone.CommercialHigh;
                        break;
                    case "CommercialLow":
                        zone = ItemClass.Zone.CommercialLow;
                        break;
                    case "Industrial":
                        zone = ItemClass.Zone.Industrial;
                        break;
                    case "Office":
                        zone = ItemClass.Zone.Office;
                        break;
                    case "ResidentialHigh":
                        zone = ItemClass.Zone.ResidentialHigh;
                        break;
                    case "ResidentialLow":
                        zone = ItemClass.Zone.ResidentialLow;
                        break;
                }

                //Debug.Log(zone_name);
                int xMin = a_zone.zoneBoundingBox[0];
                int xMax = a_zone.zoneBoundingBox[1];
                int yMin = a_zone.zoneBoundingBox[2];
                int yMax = a_zone.zoneBoundingBox[3];
                Debug.Log("xMin: " + xMin + ", xMax: " + xMax + ", yMin: " + yMin + ", yMax: " + yMax);
                for (int i = 0; i < (xMax - xMin); i += 10)
                    for (int j = 0; j < (yMax - yMin); j += 10)
                    {
                        Vector2 pos = new Vector2(xMin + i, yMin + j);
                        if (IsPointInPolygon(a_zone.zoneVertices, pos))
                        {
                            int zoneMapX = (int)pos.x / 120;
                            int zoneMapZ = (int)pos.y / 120;
                            string zoneMapXZ = zoneMapX + "," + zoneMapZ;
                            if (!zoneBlockMap.ContainsKey(zoneMapXZ))
                            {
                                Debug.Log("Not found in zoneBlockMap: " + zoneMapXZ);
                                continue;
                            }

                            foreach (var zoneBlockId in zoneBlockMap[zoneMapXZ])
                            {
                                int num = (int)((zoneBlocks[zoneBlockId].m_flags & 65280u) >> 8);
                                var blockPos = zoneBlocks[zoneBlockId].m_position;
                                if ((blockPos.x < pos.x + 5 && blockPos.x > pos.x - 5) &&
                                    (blockPos.z < pos.y + 5 && blockPos.z > pos.y - 5))
                                {                                    
                                    for (int z = 0; z < num; z++)
                                        for (int x = 0; x < 4; x++)
                                        {
                                            if (zoneBlocks[zoneBlockId].SetZone(x, z, zone))
                                                Debug.Log("Zone set to: " + zone);
                                        }
                                    zoneBlocks[zoneBlockId].RefreshZoning((ushort)zoneBlockId);
                                    Debug.Log("Zones set for " + zoneBlockId);
                                }
                            }
                        }
                    }
            }

            panel.SetMessage("GeoSkylines", "Zones import completed. ", false);

        }

        private IEnumerator AddRoad(Randomizer rand, NetInfo ni, ushort startNode, ushort endNode, Vector3 startDirection, Vector3 endDirection, string street_name)
        {
            ushort segmentId;         
            try
            {
                if (netManager.CreateSegment(out segmentId, ref rand, ni, startNode, endNode, startDirection, endDirection, Singleton<SimulationManager>.instance.m_currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, false))
                {
                    Singleton<SimulationManager>.instance.m_currentBuildIndex += 2u;                    
                    if (street_name != "")
                        netManager.SetSegmentNameImpl(segmentId, street_name);

                }
            }
            catch (Exception ex)
            {
                //try-catch just to prevent crashing by ignoring invalid trees and letting valid trees get created
                //RaiseTreeMapperEvent (ex.Message);
            }
            yield return null;
        }

        //private IEnumerator AddRail(Randomizer rand, NetInfo ni, ushort startNode, ushort endNode, Vector3 startDirection, Vector3 endDirection)
        //{
        //    ushort segmentId;
        //    NetManager net_manager = NetManager.instance;
        //    try
        //    {
        //        if (net_manager.CreateSegment(out segmentId, ref rand, ni, startNode, endNode, startDirection, endDirection, Singleton<SimulationManager>.instance.m_currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, false))
        //        {
        //            Singleton<SimulationManager>.instance.m_currentBuildIndex += 2u;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        //try-catch just to prevent crashing by ignoring invalid trees and letting valid trees get created
        //        //RaiseTreeMapperEvent (ex.Message);
        //    }
        //    yield return null;
        //}

        private IEnumerator AddBuilding(BuildingInfo bi, Vector3 bldPos, float angle)
        {
            try
            {

                if (buildingManager.CreateBuilding(out ushort bldId, ref rand, bi, bldPos, angle, bi.GetLength(), Singleton<SimulationManager>.instance.m_currentBuildIndex))
                {
                    ++Singleton<SimulationManager>.instance.m_currentBuildIndex;
                    //Debug.Log("New segment ID: " + segmentId.ToString() + " and name: " + net_manager.GetSegmentName(segmentId));
                }
            }
            catch (Exception ex)
            {
                //try-catch just to prevent crashing by ignoring invalid trees and letting valid trees get created
                //RaiseTreeMapperEvent (ex.Message);
            }
            yield return null;
        }

        private IEnumerator AddTree(Vector3 treePos, Randomizer rr, TreeInfo treeI)
        {
            uint treeNum;
            TreeManager tree_manager = TreeManager.instance;
            try
            {
                if (!tree_manager.CreateTree(out treeNum, ref rr, treeI, treePos, false))
                    Debug.Log("Didn't create tree");
            }
            catch (Exception ex)
            {
                //try-catch just to prevent crashing by ignoring invalid trees and letting valid trees get created
                //RaiseTreeMapperEvent (ex.Message);
            }
            yield return null;
        }

        static void ProvideCoordsString(out string coords, string wkt)
        {
            coords = wkt;
            coords = coords.Replace("POINT", "");
            coords = coords.Replace("LINESTRING", "");
            coords = coords.Replace("LINEARRING", "");
            coords = coords.Replace("POLYGON", "");
            coords = coords.Replace(" (", "");
            coords = coords.Replace("(", "");
            coords = coords.Replace(")", "");
            if (coords.Contains("\""))
                coords = coords.Replace("\"", "");
        }

        public string OutputConfiguration(string type)
        {
            string confTxt = "";

            confTxt += "MapName: " + mapName;
            confTxt += "(" + centerLon + ", " + centerLat + ")\n";

            if (type=="road")
            {
                confTxt += "ImportRoadsCoordMax: " + impRoadsCoordMax + "\n";
                confTxt += "impRoadsFileName: " + impRoadsFileName + "\n";
                confTxt += "roadMappingFileName: " + roadMappingFileName + "\n";
            }

            if (type=="rail")
            {
                confTxt += "ImportRailsCoordMax: " + impRailsCoordMax + "\n";
                confTxt += "impRailsFileName: " + impRailsFileName + "\n";
                confTxt += "railMappingFileName: " + railMappingFileName + "\n";
            }

            if (type=="zone")
            {
                confTxt += "ImportZonesCoordMax: " + impZonesCoordMax + "\n";
                confTxt += "impZonesFileName: " + impZonesFileName + "\n";
                confTxt += "zoneMatchFileName: " + zoneMappingFileName + "\n";
            }

            if (type=="service")
            {
                confTxt += "coordMax: " + coordMax + "\n";
                confTxt += "impServicesFileName: " + impServicesFileName + "\n";
                confTxt += "serviceMatchFileName: " + serviceMatchFileName + "\n";
            }

            if (type=="tree1")
            {
                confTxt += "ImportTreesCoordMax: " + impTreesCoordMax + "\n";
                confTxt += "ImportTreesTreeTypes: ";
                foreach (var treeT in treeTypes)
                    confTxt += treeT + ", ";
                confTxt += "\n";
                confTxt += "impTreesRasterFileName: " + impTreesRasterFileName + "\n";
                confTxt += "ImportTreesRasterOffsetX: " + impTreesRasterOffsetX + "\n";
                confTxt += "ImportTreesRasterOffsetY: " + impTreesRasterOffsetY + "\n";
                confTxt += "ImportTreesRasterMultiply: " + impTreesRasterMultiply + "\n";
            }

            if (type == "tree2")
            {
                confTxt += "ImportTreesCoordMax: " + impTreesCoordMax + "\n";
                confTxt += "impTreesVectorFileName: " + impTreesVectorFileName + "\n";
                confTxt += "ImportTreesTreeTypes: ";
                foreach (var treeT in treeTypes)
                    confTxt += treeT + ", ";
                confTxt += "\n";
            }

            if (type=="water1")
            {
                confTxt += "coordMax: " + coordMax + "\n";
                confTxt += "ImportWaterWayTypes: ";
                foreach (var waterT in impWaterWayTypes)
                    confTxt += waterT + ", ";
                confTxt += "\n";
                confTxt += "ImportWaterWayDepths: ";
                foreach (var waterD in impWaterWayDepths)
                    confTxt += waterD + ",";
                confTxt += "\n";
                confTxt += "ImportWaterWayWidths: ";
                foreach (var waterW in impWaterWayWidths)
                    confTxt += waterW + ",";
                confTxt += "\n"; 
            }

            if (type=="water2")
            {
                confTxt += "coordMax: " + coordMax + "\n";
                confTxt += "ImportWaterDepth: " + impWaterDepth + "\n"; 
            }

            return confTxt;
        }

        //public float  Distance(Vector2 start, Vector2 end)
        //{
        //    var diffX = end.x - start.x;
        //    var diffY = end.y - start.y;
        //    var length = Math.Sqrt((diffX * diffX) + (diffY * diffY));
        //    return (float)length;
        //}

        //public float Distance(Vector3 start, Vector3 end)
        //{
        //    var diffX = end.x - start.x;
        //    var diffY = end.z - start.z;
        //    var length = Math.Sqrt((diffX * diffX) + (diffY * diffY));
        //    return (float)length;
        //}
    }
}
