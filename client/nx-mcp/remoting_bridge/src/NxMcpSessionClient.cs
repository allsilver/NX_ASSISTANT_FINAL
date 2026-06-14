using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using System.Text;
using System.Threading;

public class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        AppDomain.CurrentDomain.AssemblyResolve += NxMcpSessionClient.ResolveNxAssembly;
        return NxMcpSessionClient.Run(args);
    }
}

public class NxMcpSessionClient
{
    private const int Port = 8792;

    private class WallThicknessCandidateReport
    {
        public int FaceA = -1;
        public int FaceB = -1;
        public double Distance = 0.0;
        public double Approx = 0.0;
        public double AlignmentA = 0.0;
        public double AlignmentB = 0.0;
        public double NormalDot = 0.0;
        public double UvMarginA = -1.0;
        public double UvMarginB = -1.0;
        public double SortKey = 0.0;
        public double[] PointA = new double[3];
        public double[] PointB = new double[3];
    }

    private class SectionSamplePoint
    {
        public int CurveIndex = -1;
        public int PointIndex = -1;
        public double X = 0.0;
        public double Y = 0.0;
        public double Z = 0.0;
        public double U = 0.0;
        public double V = 0.0;
        public double TangentU = 0.0;
        public double TangentV = 0.0;
    }

    private class SectionThicknessResult
    {
        public bool Found = false;
        public double Distance = Double.MaxValue;
        public SectionSamplePoint A = null;
        public SectionSamplePoint B = null;
        public string Method = "";
    }

    public static int Run(string[] args)
    {
        try
        {
            string command = args.Length > 0 ? args[0] : "status";

            try
            {
                IDictionary channelProps = new Hashtable();
                int channelTimeoutMs = 900000;
                try
                {
                    string timeoutValue = Environment.GetEnvironmentVariable("NX_MCP_HTTP_TIMEOUT_MS");
                    if (!string.IsNullOrWhiteSpace(timeoutValue))
                    {
                        channelTimeoutMs = Math.Max(5000, Int32.Parse(timeoutValue, System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                catch
                {
                }
                channelProps["timeout"] = channelTimeoutMs;
                ChannelServices.RegisterChannel(new HttpChannel(channelProps, null, null), false);
            }
            catch
            {
            }

            NXOpen.Session session = (NXOpen.Session)Activator.GetObject(
                typeof(NXOpen.Session),
                "http://127.0.0.1:" + Port + "/NXOpenSession"
            );

            if (command == "status")
            {
                Console.WriteLine(StatusJson(session));
                return 0;
            }

            if (command == "list_bodies")
            {
                Console.WriteLine(ListBodies(session));
                return 0;
            }

            if (command == "list_features")
            {
                int limit = args.Length > 1 ? Int32.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 20;
                Console.WriteLine(ListFeatures(session, limit));
                return 0;
            }

            if (command == "analyze_bodies")
            {
                string targetBodyName = args.Length > 1 ? args[1] : "";
                NXOpen.UF.UFSession ufSession = (NXOpen.UF.UFSession)Activator.GetObject(
                    typeof(NXOpen.UF.UFSession),
                    "http://127.0.0.1:" + Port + "/UFSession"
                );
                Console.WriteLine(AnalyzeBodies(session, ufSession, targetBodyName));
                return 0;
            }

            if (command == "validate_body_dimensions")
            {
                string targetBodyName = args.Length > 1 ? args[1] : "";
                double expectedX = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double expectedY = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double expectedZ = args.Length > 4 ? Double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double tolerance = args.Length > 5 ? Double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 0.01;
                NXOpen.UF.UFSession ufSession = (NXOpen.UF.UFSession)Activator.GetObject(
                    typeof(NXOpen.UF.UFSession),
                    "http://127.0.0.1:" + Port + "/UFSession"
                );
                Console.WriteLine(ValidateBodyDimensions(session, ufSession, targetBodyName, expectedX, expectedY, expectedZ, tolerance));
                return 0;
            }

            if (command == "color_thinnest_wall_face")
            {
                string targetBodyName = args.Length > 1 ? args[1] : "";
                int colorIndex = args.Length > 2 ? Int32.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 186;
                double minCandidateThickness = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0.01;
                int maxCandidates = args.Length > 4 ? Int32.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 5000;
                int maxExactPairs = args.Length > 5 ? Int32.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 1500;
                bool skipBlendFaces = args.Length > 6 ? ParseBool(args[6], true) : true;
                bool sourceHoleFacesOnly = args.Length > 7 ? ParseBool(args[7], true) : true;
                int reportCandidateCount = args.Length > 8 ? Int32.Parse(args[8], System.Globalization.CultureInfo.InvariantCulture) : 12;
                double debugExpectedThickness = args.Length > 9 ? Double.Parse(args[9], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double debugExpectedTolerance = args.Length > 10 ? Double.Parse(args[10], System.Globalization.CultureInfo.InvariantCulture) : 0.05;
                double minFaceUvMargin = args.Length > 11 ? Double.Parse(args[11], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                bool stableWallFacesOnly = args.Length > 12 ? ParseBool(args[12], true) : true;
                double maxRuntimeSec = args.Length > 13 ? Double.Parse(args[13], System.Globalization.CultureInfo.InvariantCulture) : 240.0;
                NXOpen.UF.UFSession ufSession = (NXOpen.UF.UFSession)Activator.GetObject(
                    typeof(NXOpen.UF.UFSession),
                    "http://127.0.0.1:" + Port + "/UFSession"
                );
                Console.WriteLine(ColorThinnestWallFaceByAabbCandidates(session, ufSession, targetBodyName, colorIndex, minCandidateThickness, maxCandidates, maxExactPairs, skipBlendFaces, sourceHoleFacesOnly, reportCandidateCount, debugExpectedThickness, debugExpectedTolerance, minFaceUvMargin, stableWallFacesOnly, maxRuntimeSec));
                return 0;
            }

            if (command == "sketch")
            {
                string sketchName = args.Length > 1 ? args[1] : "MCP Session Sketch";
                double width = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 50.0;
                double height = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : width;
                Console.WriteLine(CreateBasicSketch(session, sketchName, width, height));
                return 0;
            }

            if (command == "curves")
            {
                string curveSetName = args.Length > 1 ? args[1] : "MCP Rectangle Curves";
                double width = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 50.0;
                double height = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : width;
                Console.WriteLine(CreateRectangleCurves(session, curveSetName, width, height));
                return 0;
            }

            if (command == "line_curve")
            {
                string name = args.Length > 1 ? args[1] : "MCP Line";
                double x1 = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double y1 = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double x2 = args.Length > 4 ? Double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 50.0;
                double y2 = args.Length > 5 ? Double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                Console.WriteLine(CreateLineCurve(session, name, x1, y1, x2, y2));
                return 0;
            }

            if (command == "circle_curve")
            {
                string name = args.Length > 1 ? args[1] : "MCP Circle";
                double centerX = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double centerY = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double radius = args.Length > 4 ? Double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 10.0;
                Console.WriteLine(CreateCircleCurve(session, name, centerX, centerY, radius));
                return 0;
            }

            if (command == "reference_cross")
            {
                string name = args.Length > 1 ? args[1] : "MCP Reference Cross";
                double size = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 50.0;
                Console.WriteLine(CreateReferenceCross(session, name, size));
                return 0;
            }

            if (command == "box_body")
            {
                string name = args.Length > 1 ? args[1] : "MCP Box Body";
                double originX = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double originY = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double originZ = args.Length > 4 ? Double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double length = args.Length > 5 ? Double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 50.0;
                double width = args.Length > 6 ? Double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 30.0;
                double height = args.Length > 7 ? Double.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture) : 10.0;
                Console.WriteLine(CreateBoxBody(session, name, originX, originY, originZ, length, width, height));
                return 0;
            }

            if (command == "extrude_rectangle")
            {
                string name = args.Length > 1 ? args[1] : "MCP Extruded Rectangle";
                double centerX = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double centerY = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double originZ = args.Length > 4 ? Double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double width = args.Length > 5 ? Double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 50.0;
                double height = args.Length > 6 ? Double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 30.0;
                double depth = args.Length > 7 ? Double.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture) : 10.0;
                Console.WriteLine(CreateExtrudedRectangle(session, name, centerX, centerY, originZ, width, height, depth));
                return 0;
            }

            if (command == "section_slice")
            {
                string targetBodyName = args.Length > 1 ? args[1] : "";
                double planeX = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double planeY = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 58.0;
                double planeZ = args.Length > 4 ? Double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double normalX = args.Length > 5 ? Double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                double normalY = args.Length > 6 ? Double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 1.0;
                double normalZ = args.Length > 7 ? Double.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
                int samplesPerCurve = args.Length > 8 ? Int32.Parse(args[8], System.Globalization.CultureInfo.InvariantCulture) : 48;
                double minCandidateThickness = args.Length > 9 ? Double.Parse(args[9], System.Globalization.CultureInfo.InvariantCulture) : 0.03;
                string outputDir = args.Length > 10 ? args[10] : "";
                Console.WriteLine(RunSectionSliceInNxProcess(session, targetBodyName, planeX, planeY, planeZ, normalX, normalY, normalZ, samplesPerCurve, minCandidateThickness, outputDir));
                return 0;
            }

            if (command == "hinge_section")
            {
                string sectionName = args.Length > 1 ? args[1] : "MEG Hinge Housing Section";
                double width = args.Length > 2 ? Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 80.0;
                double height = args.Length > 3 ? Double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 12.0;
                double springWall = args.Length > 4 ? Double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0.38;
                double screwWall = args.Length > 5 ? Double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 0.50;
                double fpcbFloor = args.Length > 6 ? Double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 0.40;
                double sideWall = args.Length > 7 ? Double.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture) : screwWall;
                string sourceNote = args.Length > 8 ? args[8] : "";
                Console.WriteLine(CreateHingeHousingSection(session, sectionName, width, height, springWall, screwWall, fpcbFloor, sideWall, sourceNote));
                return 0;
            }

            Console.WriteLine("{\"ok\":false,\"error\":\"Usage: NxMcpSessionClient.exe [status|list_bodies|list_features|analyze_bodies|validate_body_dimensions|color_thinnest_wall_face|sketch|curves|line_curve|circle_curve|reference_cross|box_body|extrude_rectangle|section_slice|hinge_section] ...\"}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("{\"ok\":false,\"error\":\"" + JsonEscape(SafeExceptionMessage(ex)) + "\"}");
            return 1;
        }
    }

    private static string StatusJson(NXOpen.Session session)
    {
        NXOpen.Part workPart = session.Parts.Work;
        NXOpen.Part displayPart = session.Parts.Display;
        return "{"
            + "\"ok\":true,"
            + "\"bridge\":\"nx-session-remoting\","
            + "\"port\":" + Port + ","
            + "\"has_work_part\":" + BoolJson(workPart != null) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart == null ? "" : workPart.Name) + "\","
            + "\"work_part_full_path\":\"" + JsonEscape(workPart == null ? "" : workPart.FullPath) + "\","
            + "\"display_part_name\":\"" + JsonEscape(displayPart == null ? "" : displayPart.Name) + "\""
            + "}";
    }

    private static string ListBodies(NXOpen.Session session)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        NXOpen.Body[] bodies = workPart.Bodies.ToArray();
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"ok\":true,");
        sb.Append("\"work_part_name\":\"").Append(JsonEscape(workPart.Name)).Append("\",");
        sb.Append("\"body_count\":").Append(bodies.Length).Append(",");
        sb.Append("\"bodies\":[");
        for (int i = 0; i < bodies.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(",");
            }
            NXOpen.Body body = bodies[i];
            string name = "";
            string journalId = "";
            bool isSolid = false;
            bool isSheet = false;
            try { name = body.Name; } catch { }
            try { journalId = body.JournalIdentifier; } catch { }
            try { isSolid = body.IsSolidBody; } catch { }
            try { isSheet = body.IsSheetBody; } catch { }

            sb.Append("{");
            sb.Append("\"index\":").Append(i).Append(",");
            sb.Append("\"name\":\"").Append(JsonEscape(name)).Append("\",");
            sb.Append("\"journal_id\":\"").Append(JsonEscape(journalId)).Append("\",");
            sb.Append("\"is_solid\":").Append(BoolJson(isSolid)).Append(",");
            sb.Append("\"is_sheet\":").Append(BoolJson(isSheet));
            sb.Append("}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string AnalyzeBodies(NXOpen.Session session, NXOpen.UF.UFSession ufSession, string targetBodyName)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string target = string.IsNullOrWhiteSpace(targetBodyName) ? "" : targetBodyName.Trim();
        NXOpen.Body[] bodies = workPart.Bodies.ToArray();

        StringBuilder sb = new StringBuilder();
        int returnedCount = 0;
        sb.Append("{\"ok\":true,");
        sb.Append("\"work_part_name\":\"").Append(JsonEscape(workPart.Name)).Append("\",");
        sb.Append("\"target_body_name\":\"").Append(JsonEscape(target)).Append("\",");
        sb.Append("\"body_count\":").Append(bodies.Length).Append(",");
        sb.Append("\"bodies\":[");

        for (int i = 0; i < bodies.Length; i++)
        {
            NXOpen.Body body = bodies[i];
            string name = "";
            string journalId = "";
            bool isSolid = false;
            bool isSheet = false;
            try { name = body.Name; } catch { }
            try { journalId = body.JournalIdentifier; } catch { }
            try { isSolid = body.IsSolidBody; } catch { }
            try { isSheet = body.IsSheetBody; } catch { }

            if (!string.IsNullOrWhiteSpace(target)
                && !String.Equals(name, target, StringComparison.OrdinalIgnoreCase)
                && !String.Equals(journalId, target, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            NXOpen.Face[] faces = new NXOpen.Face[0];
            NXOpen.Edge[] edges = new NXOpen.Edge[0];
            try { faces = body.GetFaces(); } catch { }
            try { edges = body.GetEdges(); } catch { }

            bool hasExactBoundingBox = false;
            string exactBoundingBoxError = "";
            double[] exactMinCorner = new double[3];
            double[,] exactDirections = new double[3, 3];
            double[] exactDistances = new double[3];
            try
            {
                ufSession.Modl.AskBoundingBoxExact(
                    body.Tag,
                    NXOpen.Tag.Null,
                    exactMinCorner,
                    exactDirections,
                    exactDistances
                );
                hasExactBoundingBox = true;
            }
            catch (Exception ex)
            {
                exactBoundingBoxError = ShortError(ex);
            }

            bool hasMassProperties = false;
            string massPropertiesError = "";
            double surfaceArea = 0.0;
            double volume = 0.0;
            double mass = 0.0;
            double weight = 0.0;
            NXOpen.Point3d centroid = new NXOpen.Point3d();
            try
            {
                NXOpen.Unit[] units = MassPropertyUnits(workPart);
                NXOpen.IBody[] selectedBodies = new NXOpen.IBody[] { body };
                NXOpen.MeasureBodies measure = workPart.MeasureManager.NewMassProperties(units, 0.99, selectedBodies);
                surfaceArea = measure.Area;
                volume = measure.Volume;
                mass = measure.Mass;
                weight = measure.Weight;
                centroid = measure.Centroid;
                hasMassProperties = true;
                try { measure.Dispose(); } catch { }
            }
            catch (Exception ex)
            {
                massPropertiesError = ShortError(ex);
            }

            bool hasBoundingBox = false;
            double minX = 0.0;
            double minY = 0.0;
            double minZ = 0.0;
            double maxX = 0.0;
            double maxY = 0.0;
            double maxZ = 0.0;
            for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                NXOpen.Point3d first = new NXOpen.Point3d();
                NXOpen.Point3d second = new NXOpen.Point3d();
                try
                {
                    edges[edgeIndex].GetVertices(out first, out second);
                    ExtendBounds(first, ref hasBoundingBox, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                    ExtendBounds(second, ref hasBoundingBox, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                }
                catch
                {
                }
            }

            if (returnedCount > 0)
            {
                sb.Append(",");
            }
            returnedCount++;

            sb.Append("{");
            sb.Append("\"index\":").Append(i).Append(",");
            sb.Append("\"name\":\"").Append(JsonEscape(name)).Append("\",");
            sb.Append("\"journal_id\":\"").Append(JsonEscape(journalId)).Append("\",");
            sb.Append("\"is_solid\":").Append(BoolJson(isSolid)).Append(",");
            sb.Append("\"is_sheet\":").Append(BoolJson(isSheet)).Append(",");
            sb.Append("\"face_count\":").Append(faces.Length).Append(",");
            sb.Append("\"edge_count\":").Append(edges.Length).Append(",");
            sb.Append("\"has_bounding_box\":").Append(BoolJson(hasBoundingBox));
            if (hasBoundingBox)
            {
                double sizeX = maxX - minX;
                double sizeY = maxY - minY;
                double sizeZ = maxZ - minZ;
                sb.Append(",");
                sb.Append("\"bounding_box_source\":\"edge_vertices\",");
                sb.Append("\"bbox_min_mm\":[").Append(Num(minX)).Append(",").Append(Num(minY)).Append(",").Append(Num(minZ)).Append("],");
                sb.Append("\"bbox_max_mm\":[").Append(Num(maxX)).Append(",").Append(Num(maxY)).Append(",").Append(Num(maxZ)).Append("],");
                sb.Append("\"size_mm\":{");
                sb.Append("\"x\":").Append(Num(sizeX)).Append(",");
                sb.Append("\"y\":").Append(Num(sizeY)).Append(",");
                sb.Append("\"z\":").Append(Num(sizeZ));
                sb.Append("}");
            }
            sb.Append(",");
            sb.Append("\"has_exact_bounding_box\":").Append(BoolJson(hasExactBoundingBox));
            if (hasExactBoundingBox)
            {
                sb.Append(",");
                sb.Append("\"exact_bounding_box_source\":\"uf_ask_bounding_box_exact\",");
                sb.Append("\"exact_bbox_min_corner_mm\":[").Append(Num(exactMinCorner[0])).Append(",").Append(Num(exactMinCorner[1])).Append(",").Append(Num(exactMinCorner[2])).Append("],");
                sb.Append("\"exact_bbox_directions\":[");
                for (int row = 0; row < 3; row++)
                {
                    if (row > 0) { sb.Append(","); }
                    sb.Append("[")
                        .Append(Num(exactDirections[row, 0])).Append(",")
                        .Append(Num(exactDirections[row, 1])).Append(",")
                        .Append(Num(exactDirections[row, 2])).Append("]");
                }
                sb.Append("],");
                sb.Append("\"exact_size_mm\":{");
                sb.Append("\"x\":").Append(Num(exactDistances[0])).Append(",");
                sb.Append("\"y\":").Append(Num(exactDistances[1])).Append(",");
                sb.Append("\"z\":").Append(Num(exactDistances[2]));
                sb.Append("}");
            }
            else if (!string.IsNullOrWhiteSpace(exactBoundingBoxError))
            {
                sb.Append(",");
                sb.Append("\"exact_bounding_box_error\":\"").Append(JsonEscape(exactBoundingBoxError)).Append("\"");
            }
            sb.Append(",");
            sb.Append("\"has_mass_properties\":").Append(BoolJson(hasMassProperties));
            if (hasMassProperties)
            {
                sb.Append(",");
                sb.Append("\"mass_properties_source\":\"measure_manager_new_mass_properties\",");
                sb.Append("\"surface_area_mm2\":").Append(Num(surfaceArea)).Append(",");
                sb.Append("\"volume_mm3\":").Append(Num(volume)).Append(",");
                sb.Append("\"centroid_mm\":[").Append(Num(centroid.X)).Append(",").Append(Num(centroid.Y)).Append(",").Append(Num(centroid.Z)).Append("],");
                sb.Append("\"mass_kg\":").Append(Num(mass)).Append(",");
                sb.Append("\"weight_n\":").Append(Num(weight));
            }
            else if (!string.IsNullOrWhiteSpace(massPropertiesError))
            {
                sb.Append(",");
                sb.Append("\"mass_properties_error\":\"").Append(JsonEscape(massPropertiesError)).Append("\"");
            }
            sb.Append("}");
        }

        sb.Append("],");
        sb.Append("\"returned_count\":").Append(returnedCount);
        sb.Append("}");
        return sb.ToString();
    }

    private static string ValidateBodyDimensions(
        NXOpen.Session session,
        NXOpen.UF.UFSession ufSession,
        string targetBodyName,
        double expectedX,
        double expectedY,
        double expectedZ,
        double tolerance)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string target = string.IsNullOrWhiteSpace(targetBodyName) ? "" : targetBodyName.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return "{\"ok\":false,\"error\":\"target_body_name is required for dimension validation. Use nx_remoting_list_bodies first.\"}";
        }

        NXOpen.Body[] bodies = workPart.Bodies.ToArray();
        NXOpen.Body targetBody = null;
        string bodyName = "";
        string journalId = "";

        for (int i = 0; i < bodies.Length; i++)
        {
            string name = "";
            string id = "";
            try { name = bodies[i].Name; } catch { }
            try { id = bodies[i].JournalIdentifier; } catch { }
            if (String.Equals(name, target, StringComparison.OrdinalIgnoreCase)
                || String.Equals(id, target, StringComparison.OrdinalIgnoreCase))
            {
                targetBody = bodies[i];
                bodyName = name;
                journalId = id;
                break;
            }
        }

        if (targetBody == null)
        {
            StringBuilder available = new StringBuilder();
            available.Append("[");
            for (int i = 0; i < bodies.Length; i++)
            {
                if (i > 0) { available.Append(","); }
                string name = "";
                string id = "";
                try { name = bodies[i].Name; } catch { }
                try { id = bodies[i].JournalIdentifier; } catch { }
                available.Append("{\"name\":\"").Append(JsonEscape(name)).Append("\",");
                available.Append("\"journal_id\":\"").Append(JsonEscape(id)).Append("\"}");
            }
            available.Append("]");
            return "{\"ok\":false,\"error\":\"Body not found for target_body_name.\",\"target_body_name\":\""
                + JsonEscape(target)
                + "\",\"available_bodies\":"
                + available.ToString()
                + "}";
        }

        double[] minCorner = new double[3];
        double[,] directions = new double[3, 3];
        double[] distances = new double[3];
        try
        {
            ufSession.Modl.AskBoundingBoxExact(
                targetBody.Tag,
                NXOpen.Tag.Null,
                minCorner,
                directions,
                distances
            );
        }
        catch (Exception ex)
        {
            return "{\"ok\":false,\"error\":\"NX exact bounding box failed: "
                + JsonEscape(ShortError(ex))
                + "\",\"target_body_name\":\""
                + JsonEscape(target)
                + "\"}";
        }

        tolerance = Math.Abs(tolerance);
        double measuredX = distances[0];
        double measuredY = distances[1];
        double measuredZ = distances[2];
        double deltaX = measuredX - expectedX;
        double deltaY = measuredY - expectedY;
        double deltaZ = measuredZ - expectedZ;
        bool passX = Math.Abs(deltaX) <= tolerance;
        bool passY = Math.Abs(deltaY) <= tolerance;
        bool passZ = Math.Abs(deltaZ) <= tolerance;
        bool pass = passX && passY && passZ;

        return "{"
            + "\"ok\":true,"
            + "\"validation_type\":\"exact_bbox_dimensions\","
            + "\"status\":\"" + (pass ? "PASS" : "FAIL") + "\","
            + "\"pass\":" + BoolJson(pass) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\","
            + "\"target_body_name\":\"" + JsonEscape(target) + "\","
            + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
            + "\"journal_id\":\"" + JsonEscape(journalId) + "\","
            + "\"tolerance_mm\":" + Num(tolerance) + ","
            + "\"expected_size_mm\":{\"x\":" + Num(expectedX) + ",\"y\":" + Num(expectedY) + ",\"z\":" + Num(expectedZ) + "},"
            + "\"measured_size_mm\":{\"x\":" + Num(measuredX) + ",\"y\":" + Num(measuredY) + ",\"z\":" + Num(measuredZ) + "},"
            + "\"delta_mm\":{\"x\":" + Num(deltaX) + ",\"y\":" + Num(deltaY) + ",\"z\":" + Num(deltaZ) + "},"
            + "\"axis_results\":{"
            + "\"x\":{\"pass\":" + BoolJson(passX) + ",\"abs_delta_mm\":" + Num(Math.Abs(deltaX)) + "},"
            + "\"y\":{\"pass\":" + BoolJson(passY) + ",\"abs_delta_mm\":" + Num(Math.Abs(deltaY)) + "},"
            + "\"z\":{\"pass\":" + BoolJson(passZ) + ",\"abs_delta_mm\":" + Num(Math.Abs(deltaZ)) + "}"
            + "},"
            + "\"bbox_source\":\"uf_ask_bounding_box_exact\""
            + "}";
    }

    private static string ColorThinnestWallFace(
        NXOpen.Session session,
        NXOpen.UF.UFSession ufSession,
        string targetBodyName,
        int colorIndex,
        double minCandidateThickness,
        double oppositeNormalDotMax,
        int maxExactPairs,
        double normalAlignmentMin,
        bool skipBlendFaces)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        NXOpen.Body body = FindTargetBody(workPart, targetBodyName);
        if (body == null)
        {
            return "{\"ok\":false,\"error\":\"Body not found. Provide target_body_name from nx_remoting_list_bodies.\",\"target_body_name\":\""
                + JsonEscape(targetBodyName)
                + "\"}";
        }

        string bodyName = "";
        string bodyJournalId = "";
        try { bodyName = body.Name; } catch { }
        try { bodyJournalId = body.JournalIdentifier; } catch { }

        NXOpen.Face[] faces = new NXOpen.Face[0];
        try { faces = body.GetFaces(); } catch { }
        if (faces.Length < 2)
        {
            return "{\"ok\":false,\"error\":\"Target body does not have enough faces to estimate wall thickness.\",\"body_name\":\""
                + JsonEscape(bodyName)
                + "\"}";
        }

        minCandidateThickness = Math.Abs(minCandidateThickness);
        if (minCandidateThickness < 0.000001)
        {
            minCandidateThickness = 0.000001;
        }
        if (maxExactPairs < 1)
        {
            maxExactPairs = 1;
        }
        if (maxExactPairs > 1000)
        {
            maxExactPairs = 1000;
        }
        normalAlignmentMin = Math.Abs(normalAlignmentMin);
        if (normalAlignmentMin > 1.0)
        {
            normalAlignmentMin = 1.0;
        }

        int[] candidateA = new int[maxExactPairs];
        int[] candidateB = new int[maxExactPairs];
        double[] candidateApprox = new double[maxExactPairs];
        double[] candidateDot = new double[maxExactPairs];
        int candidateCount = 0;
        int approximateCandidateCount = 0;
        int oppositeNormalCandidateCount = 0;
        int skippedBlendPairCount = 0;
        int skippedLowAlignmentPairCount = 0;
        int skippedSamplePairCount = 0;
        int checkedPairCount = 0;
        int skippedTouchingPairCount = 0;
        int skippedFailedPairCount = 0;

        double[][] normals = new double[faces.Length][];
        double[][] centers = new double[faces.Length][];
        string[] faceTypes = new string[faces.Length];
        bool[] isBlendFace = new bool[faces.Length];
        bool[] hasNormal = new bool[faces.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            normals[i] = new double[3];
            centers[i] = new double[3];
            hasNormal[i] = TryFaceSampleAtMidUv(ufSession, faces[i], centers[i], normals[i]);
            try { faceTypes[i] = faces[i].SolidFaceType.ToString(); } catch { faceTypes[i] = ""; }
            isBlendFace[i] = IsNonWallDetailFaceType(faceTypes[i]);
        }

        for (int i = 0; i < faces.Length; i++)
        {
            for (int j = i + 1; j < faces.Length; j++)
            {
                checkedPairCount++;
                if (skipBlendFaces && (isBlendFace[i] || isBlendFace[j]))
                {
                    skippedBlendPairCount++;
                    continue;
                }

                if (!hasNormal[i] || !hasNormal[j])
                {
                    skippedSamplePairCount++;
                    continue;
                }

                double dot = Dot(normals[i], normals[j]);
                double dx = centers[j][0] - centers[i][0];
                double dy = centers[j][1] - centers[i][1];
                double dz = centers[j][2] - centers[i][2];
                double centerDistance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double normalProjection = Math.Abs(dx * normals[i][0] + dy * normals[i][1] + dz * normals[i][2]);
                double approximateThickness = normalProjection > minCandidateThickness ? normalProjection : centerDistance;

                if (approximateThickness <= minCandidateThickness)
                {
                    skippedTouchingPairCount++;
                    continue;
                }

                approximateCandidateCount++;
                if (dot <= oppositeNormalDotMax)
                {
                    oppositeNormalCandidateCount++;
                    InsertCandidate(candidateA, candidateB, candidateApprox, candidateDot, ref candidateCount, maxExactPairs, i, j, approximateThickness, dot);
                }
            }
        }

        if (candidateCount == 0)
        {
            return "{\"ok\":false,\"error\":\"No non-touching face pair found for wall thickness estimation.\",\"body_name\":\""
                + JsonEscape(bodyName)
                + "\",\"face_count\":"
                + faces.Length
                + ",\"checked_pair_count\":"
                + checkedPairCount
                + ",\"opposite_normal_candidate_count\":"
                + oppositeNormalCandidateCount
                + "}";
        }

        double bestDistance = Double.MaxValue;
        int bestA = -1;
        int bestB = -1;
        double bestDot = 0.0;
        double bestApprox = 0.0;
        double[] bestPointA = new double[3];
        double[] bestPointB = new double[3];
        double bestNormalAlignmentA = 0.0;
        double bestNormalAlignmentB = 0.0;
        int exactPairCount = 0;
        for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
        {
            double distance = 0.0;
            double[] pointA = new double[3];
            double[] pointB = new double[3];
            try
            {
                exactPairCount++;
                ufSession.Modl.AskMinimumDist(
                    faces[candidateA[candidateIndex]].Tag,
                    faces[candidateB[candidateIndex]].Tag,
                    0,
                    new double[3],
                    0,
                    new double[3],
                    out distance,
                    pointA,
                    pointB
                );
            }
            catch
            {
                skippedFailedPairCount++;
                continue;
            }

            if (distance <= minCandidateThickness)
            {
                skippedTouchingPairCount++;
                continue;
            }

            double vectorLength = Distance(pointA, pointB);
            if (vectorLength <= 0.0000001)
            {
                skippedTouchingPairCount++;
                continue;
            }
            double[] thicknessDirection = new double[]
            {
                (pointB[0] - pointA[0]) / vectorLength,
                (pointB[1] - pointA[1]) / vectorLength,
                (pointB[2] - pointA[2]) / vectorLength
            };
            double alignmentA = Math.Abs(Dot(thicknessDirection, normals[candidateA[candidateIndex]]));
            double alignmentB = Math.Abs(Dot(thicknessDirection, normals[candidateB[candidateIndex]]));
            if (alignmentA < normalAlignmentMin || alignmentB < normalAlignmentMin)
            {
                skippedLowAlignmentPairCount++;
                continue;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestA = candidateA[candidateIndex];
                bestB = candidateB[candidateIndex];
                bestDot = candidateDot[candidateIndex];
                bestApprox = candidateApprox[candidateIndex];
                CopyPoint(pointA, bestPointA);
                CopyPoint(pointB, bestPointB);
                bestNormalAlignmentA = alignmentA;
                bestNormalAlignmentB = alignmentB;
            }
        }

        if (bestA < 0 || bestB < 0)
        {
            return "{\"ok\":false,\"error\":\"Exact minimum-distance checks did not find a valid non-touching face pair.\",\"body_name\":\""
                + JsonEscape(bodyName)
                + "\",\"face_count\":"
                + faces.Length
                + ",\"candidate_count\":"
                + candidateCount
                + ",\"exact_pair_count\":"
                + exactPairCount
                + "}";
        }

        string faceAName = "";
        string faceBName = "";
        string faceAJournalId = "";
        string faceBJournalId = "";
        string faceAType = "";
        string faceBType = "";
        try { faceAName = faces[bestA].Name; } catch { }
        try { faceBName = faces[bestB].Name; } catch { }
        try { faceAJournalId = faces[bestA].JournalIdentifier; } catch { }
        try { faceBJournalId = faces[bestB].JournalIdentifier; } catch { }
        try { faceAType = faces[bestA].SolidFaceType.ToString(); } catch { }
        try { faceBType = faces[bestB].SolidFaceType.ToString(); } catch { }

        bool colored = false;
        string colorError = "";
        try
        {
            NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
                NXOpen.Session.MarkVisibility.Visible,
                "MCP color thinnest wall face pair"
            );
            NXOpen.DisplayModification displayModification = session.DisplayManager.NewDisplayModification();
            displayModification.ApplyToAllFaces = false;
            displayModification.NewColor = colorIndex;
            displayModification.Apply(new NXOpen.DisplayableObject[] { faces[bestA], faces[bestB] });
            displayModification.Dispose();
            try { faces[bestA].SetName("MCP_MIN_WALL_FACE_A_" + FormatMm(bestDistance)); } catch { }
            try { faces[bestB].SetName("MCP_MIN_WALL_FACE_B_" + FormatMm(bestDistance)); } catch { }
            session.UpdateManager.DoUpdate(markId);
            try { faces[bestA].RedisplayObject(); } catch { }
            try { faces[bestB].RedisplayObject(); } catch { }
            colored = true;
        }
        catch (Exception ex)
        {
            colorError = ShortError(ex);
        }

        return "{"
            + "\"ok\":true,"
            + "\"analysis_type\":\"thinnest_wall_face_pair\","
            + "\"method\":\"normal_aligned_face_pair_minimum_distance\","
            + "\"note\":\"POC estimate. It excludes blend faces by default, prefers strongly opposite normals, and requires the exact shortest-distance vector to align with both face normals. Complex ribs and local wall-thickness maps still need denser sampling later.\","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\","
            + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
            + "\"body_journal_id\":\"" + JsonEscape(bodyJournalId) + "\","
            + "\"face_count\":" + faces.Length + ","
            + "\"checked_pair_count\":" + checkedPairCount + ","
            + "\"skipped_touching_pair_count\":" + skippedTouchingPairCount + ","
            + "\"skipped_blend_pair_count\":" + skippedBlendPairCount + ","
            + "\"skipped_sample_pair_count\":" + skippedSamplePairCount + ","
            + "\"skipped_low_alignment_pair_count\":" + skippedLowAlignmentPairCount + ","
            + "\"skipped_failed_pair_count\":" + skippedFailedPairCount + ","
            + "\"approximate_candidate_count\":" + approximateCandidateCount + ","
            + "\"opposite_normal_candidate_count\":" + oppositeNormalCandidateCount + ","
            + "\"candidate_count\":" + candidateCount + ","
            + "\"exact_pair_count\":" + exactPairCount + ","
            + "\"opposite_normal_dot_max\":" + Num(oppositeNormalDotMax) + ","
            + "\"normal_alignment_min\":" + Num(normalAlignmentMin) + ","
            + "\"skip_blend_faces\":" + BoolJson(skipBlendFaces) + ","
            + "\"min_candidate_thickness_mm\":" + Num(minCandidateThickness) + ","
            + "\"min_thickness_mm\":" + Num(bestDistance) + ","
            + "\"approx_thickness_mm\":" + Num(bestApprox) + ","
            + "\"normal_dot\":" + Num(bestDot) + ","
            + "\"normal_alignment\":{\"face_a\":" + Num(bestNormalAlignmentA) + ",\"face_b\":" + Num(bestNormalAlignmentB) + "},"
            + "\"colored\":" + BoolJson(colored) + ","
            + "\"color_index\":" + colorIndex + ","
            + "\"color_error\":\"" + JsonEscape(colorError) + "\","
            + "\"face_a\":{"
            + "\"index\":" + bestA + ","
            + "\"name\":\"" + JsonEscape(faceAName) + "\","
            + "\"journal_id\":\"" + JsonEscape(faceAJournalId) + "\","
            + "\"face_type\":\"" + JsonEscape(faceAType) + "\","
            + "\"nearest_point_mm\":[" + Num(bestPointA[0]) + "," + Num(bestPointA[1]) + "," + Num(bestPointA[2]) + "]"
            + "},"
            + "\"face_b\":{"
            + "\"index\":" + bestB + ","
            + "\"name\":\"" + JsonEscape(faceBName) + "\","
            + "\"journal_id\":\"" + JsonEscape(faceBJournalId) + "\","
            + "\"face_type\":\"" + JsonEscape(faceBType) + "\","
            + "\"nearest_point_mm\":[" + Num(bestPointB[0]) + "," + Num(bestPointB[1]) + "," + Num(bestPointB[2]) + "]"
            + "}"
            + "}";
    }

    private static string ColorThinnestWallFaceByRay(
        NXOpen.Session session,
        NXOpen.UF.UFSession ufSession,
        string targetBodyName,
        int colorIndex,
        double minCandidateThickness,
        int sampleCount,
        int maxRayHits,
        bool skipBlendFaces,
        bool sourceHoleFacesOnly)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        NXOpen.Body body = FindTargetBody(workPart, targetBodyName);
        if (body == null)
        {
            return "{\"ok\":false,\"error\":\"Body not found. Provide target_body_name from nx_remoting_list_bodies.\",\"target_body_name\":\""
                + JsonEscape(targetBodyName)
                + "\"}";
        }

        sampleCount = Math.Max(2, Math.Min(sampleCount, 7));
        maxRayHits = Math.Max(2, Math.Min(maxRayHits, 100));
        minCandidateThickness = Math.Abs(minCandidateThickness);
        if (minCandidateThickness < 0.000001)
        {
            minCandidateThickness = 0.000001;
        }

        string bodyName = "";
        string bodyJournalId = "";
        try { bodyName = body.Name; } catch { }
        try { bodyJournalId = body.JournalIdentifier; } catch { }

        NXOpen.Face[] faces = new NXOpen.Face[0];
        try { faces = body.GetFaces(); } catch { }
        if (faces.Length < 2)
        {
            return "{\"ok\":false,\"error\":\"Target body does not have enough faces to estimate wall thickness.\",\"body_name\":\""
                + JsonEscape(bodyName)
                + "\"}";
        }

        double bestDistance = Double.MaxValue;
        int bestSourceFaceIndex = -1;
        int bestHitFaceIndex = -1;
        double[] bestSourcePoint = new double[3];
        double[] bestHitPoint = new double[3];
        double[] bestDirection = new double[3];
        double[] bestSourceNormal = new double[3];
        double[] bestHitNormal = new double[3];
        string bestSourceFaceType = "";
        string bestHitFaceType = "";
        int sampledFaceCount = 0;
        int skippedBlendFaceCount = 0;
        int skippedSourceTypeFaceCount = 0;
        int samplePointCount = 0;
        int rayCount = 0;
        int rayHitCount = 0;
        int skippedSameFaceHitCount = 0;
        int failedFaceSampleCount = 0;
        int failedRayCount = 0;

        NXOpen.Tag[] bodyTags = new NXOpen.Tag[] { body.Tag };
        double[] identity = IdentityTransform();

        for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            string faceType = "";
            try { faceType = faces[faceIndex].SolidFaceType.ToString(); } catch { }
            bool isBlendFace = faceType.IndexOf("Blend", StringComparison.OrdinalIgnoreCase) >= 0;
            if (skipBlendFaces && isBlendFace)
            {
                skippedBlendFaceCount++;
                continue;
            }
            if (sourceHoleFacesOnly && !IsHoleLikeFaceType(faceType))
            {
                skippedSourceTypeFaceCount++;
                continue;
            }

            double[] uvMinMax = new double[4];
            try
            {
                ufSession.Modl.AskFaceUvMinmax(faces[faceIndex].Tag, uvMinMax);
            }
            catch
            {
                failedFaceSampleCount++;
                continue;
            }

            sampledFaceCount++;
            int samplesU = sampleCount;
            int samplesV = sampleCount;
            if (faceType.IndexOf("Revolution", StringComparison.OrdinalIgnoreCase) >= 0
                || faceType.IndexOf("Cylind", StringComparison.OrdinalIgnoreCase) >= 0
                || faceType.IndexOf("Conical", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                samplesU = Math.Max(sampleCount * 4, 10);
                samplesV = sampleCount;
            }

            for (int uIndex = 0; uIndex < samplesU; uIndex++)
            {
                double u = uvMinMax[0] + (uvMinMax[1] - uvMinMax[0]) * ((uIndex + 1.0) / (samplesU + 1.0));
                for (int vIndex = 0; vIndex < samplesV; vIndex++)
                {
                    double v = uvMinMax[2] + (uvMinMax[3] - uvMinMax[2]) * ((vIndex + 1.0) / (samplesV + 1.0));
                    double[] sourcePoint = new double[3];
                    double[] sourceNormal = new double[3];
                    if (!TryFacePointNormalAtUv(ufSession, faces[faceIndex], u, v, sourcePoint, sourceNormal))
                    {
                        failedFaceSampleCount++;
                        continue;
                    }
                    samplePointCount++;

                    for (int directionSign = -1; directionSign <= 1; directionSign += 2)
                    {
                        double[] direction = new double[]
                        {
                            sourceNormal[0] * directionSign,
                            sourceNormal[1] * directionSign,
                            sourceNormal[2] * directionSign
                        };
                        double[] origin = new double[]
                        {
                            sourcePoint[0] + direction[0] * 0.001,
                            sourcePoint[1] + direction[1] * 0.001,
                            sourcePoint[2] + direction[2] * 0.001
                        };

                        int numResults = 0;
                        NXOpen.UF.UFModl.RayHitPointInfo[] hits = null;
                        try
                        {
                            rayCount++;
                            ufSession.Modl.TraceARay(
                                1,
                                bodyTags,
                                origin,
                                direction,
                                identity,
                                maxRayHits,
                                out numResults,
                                out hits
                            );
                        }
                        catch
                        {
                            failedRayCount++;
                            continue;
                        }

                        if (hits == null || numResults <= 0)
                        {
                            continue;
                        }
                        rayHitCount += numResults;

                        for (int hitIndex = 0; hitIndex < numResults && hitIndex < hits.Length; hitIndex++)
                        {
                            int hitFaceIndex = FindFaceIndexByTag(faces, hits[hitIndex].hit_face);
                            if (hitFaceIndex < 0)
                            {
                                continue;
                            }
                            if (hitFaceIndex == faceIndex)
                            {
                                skippedSameFaceHitCount++;
                                continue;
                            }

                            double distance = Distance(sourcePoint, hits[hitIndex].hit_point);
                            if (distance <= minCandidateThickness)
                            {
                                continue;
                            }

                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                bestSourceFaceIndex = faceIndex;
                                bestHitFaceIndex = hitFaceIndex;
                                CopyPoint(sourcePoint, bestSourcePoint);
                                CopyPoint(hits[hitIndex].hit_point, bestHitPoint);
                                CopyPoint(direction, bestDirection);
                                CopyPoint(sourceNormal, bestSourceNormal);
                                CopyPoint(hits[hitIndex].hit_normal, bestHitNormal);
                                bestSourceFaceType = faceType;
                                try { bestHitFaceType = faces[hitFaceIndex].SolidFaceType.ToString(); } catch { bestHitFaceType = ""; }
                            }
                        }
                    }
                }
            }
        }

        if (bestSourceFaceIndex < 0 || bestHitFaceIndex < 0)
        {
            return "{\"ok\":false,\"error\":\"Ray wall-thickness scan did not find an opposite face hit.\","
                + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
                + "\"face_count\":" + faces.Length + ","
                + "\"sampled_face_count\":" + sampledFaceCount + ","
                + "\"sample_point_count\":" + samplePointCount + ","
                + "\"ray_count\":" + rayCount + ","
                + "\"ray_hit_count\":" + rayHitCount
                + "}";
        }

        bool colored = false;
        string colorError = "";
        try
        {
            NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
                NXOpen.Session.MarkVisibility.Visible,
                "MCP color ray thinnest wall face pair"
            );
            NXOpen.DisplayModification displayModification = session.DisplayManager.NewDisplayModification();
            displayModification.ApplyToAllFaces = false;
            displayModification.NewColor = colorIndex;
            displayModification.Apply(new NXOpen.DisplayableObject[] { faces[bestSourceFaceIndex], faces[bestHitFaceIndex] });
            displayModification.Dispose();
            try { faces[bestSourceFaceIndex].SetName("MCP_RAY_MIN_WALL_FACE_A_" + FormatMm(bestDistance)); } catch { }
            try { faces[bestHitFaceIndex].SetName("MCP_RAY_MIN_WALL_FACE_B_" + FormatMm(bestDistance)); } catch { }
            session.UpdateManager.DoUpdate(markId);
            try { faces[bestSourceFaceIndex].RedisplayObject(); } catch { }
            try { faces[bestHitFaceIndex].RedisplayObject(); } catch { }
            colored = true;
        }
        catch (Exception ex)
        {
            colorError = ShortError(ex);
        }

        string sourceFaceName = "";
        string hitFaceName = "";
        string sourceFaceJournalId = "";
        string hitFaceJournalId = "";
        try { sourceFaceName = faces[bestSourceFaceIndex].Name; } catch { }
        try { hitFaceName = faces[bestHitFaceIndex].Name; } catch { }
        try { sourceFaceJournalId = faces[bestSourceFaceIndex].JournalIdentifier; } catch { }
        try { hitFaceJournalId = faces[bestHitFaceIndex].JournalIdentifier; } catch { }

        return "{"
            + "\"ok\":true,"
            + "\"analysis_type\":\"ray_wall_thickness\","
            + "\"method\":\"sample_face_points_and_trace_normals\","
            + "\"note\":\"POC ray-based wall thickness scan. It samples face UV points and traces rays along both normal directions to find the nearest opposite face. Increase sample_count for more precision.\","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\","
            + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
            + "\"body_journal_id\":\"" + JsonEscape(bodyJournalId) + "\","
            + "\"face_count\":" + faces.Length + ","
            + "\"sample_count\":" + sampleCount + ","
            + "\"source_hole_faces_only\":" + BoolJson(sourceHoleFacesOnly) + ","
            + "\"sampled_face_count\":" + sampledFaceCount + ","
            + "\"skipped_blend_face_count\":" + skippedBlendFaceCount + ","
            + "\"skipped_source_type_face_count\":" + skippedSourceTypeFaceCount + ","
            + "\"sample_point_count\":" + samplePointCount + ","
            + "\"ray_count\":" + rayCount + ","
            + "\"ray_hit_count\":" + rayHitCount + ","
            + "\"skipped_same_face_hit_count\":" + skippedSameFaceHitCount + ","
            + "\"failed_face_sample_count\":" + failedFaceSampleCount + ","
            + "\"failed_ray_count\":" + failedRayCount + ","
            + "\"min_thickness_mm\":" + Num(bestDistance) + ","
            + "\"colored\":" + BoolJson(colored) + ","
            + "\"color_index\":" + colorIndex + ","
            + "\"color_error\":\"" + JsonEscape(colorError) + "\","
            + "\"source_face\":{"
            + "\"index\":" + bestSourceFaceIndex + ","
            + "\"name\":\"" + JsonEscape(sourceFaceName) + "\","
            + "\"journal_id\":\"" + JsonEscape(sourceFaceJournalId) + "\","
            + "\"face_type\":\"" + JsonEscape(bestSourceFaceType) + "\","
            + "\"sample_point_mm\":[" + Num(bestSourcePoint[0]) + "," + Num(bestSourcePoint[1]) + "," + Num(bestSourcePoint[2]) + "],"
            + "\"sample_normal\":[" + Num(bestSourceNormal[0]) + "," + Num(bestSourceNormal[1]) + "," + Num(bestSourceNormal[2]) + "]"
            + "},"
            + "\"hit_face\":{"
            + "\"index\":" + bestHitFaceIndex + ","
            + "\"name\":\"" + JsonEscape(hitFaceName) + "\","
            + "\"journal_id\":\"" + JsonEscape(hitFaceJournalId) + "\","
            + "\"face_type\":\"" + JsonEscape(bestHitFaceType) + "\","
            + "\"hit_point_mm\":[" + Num(bestHitPoint[0]) + "," + Num(bestHitPoint[1]) + "," + Num(bestHitPoint[2]) + "],"
            + "\"hit_normal\":[" + Num(bestHitNormal[0]) + "," + Num(bestHitNormal[1]) + "," + Num(bestHitNormal[2]) + "]"
            + "},"
            + "\"ray_direction\":[" + Num(bestDirection[0]) + "," + Num(bestDirection[1]) + "," + Num(bestDirection[2]) + "]"
            + "}";
    }

    private static string ColorThinnestWallFaceByAabbCandidates(
        NXOpen.Session session,
        NXOpen.UF.UFSession ufSession,
        string targetBodyName,
        int colorIndex,
        double minCandidateThickness,
        int maxCandidates,
        int maxExactPairs,
        bool skipBlendFaces,
        bool sourceHoleFacesOnly,
        int reportCandidateCount,
        double debugExpectedThickness,
        double debugExpectedTolerance,
        double minFaceUvMargin,
        bool stableWallFacesOnly,
        double maxRuntimeSec)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        NXOpen.Body body = FindTargetBody(workPart, targetBodyName);
        if (body == null)
        {
            return "{\"ok\":false,\"error\":\"Body not found. Provide target_body_name from nx_remoting_list_bodies.\",\"target_body_name\":\""
                + JsonEscape(targetBodyName)
                + "\"}";
        }

        maxCandidates = Math.Max(100, Math.Min(maxCandidates, 60000));
        maxExactPairs = Math.Max(20, Math.Min(maxExactPairs, maxCandidates));
        reportCandidateCount = Math.Max(0, Math.Min(reportCandidateCount, 30));
        debugExpectedThickness = Math.Abs(debugExpectedThickness);
        debugExpectedTolerance = Math.Abs(debugExpectedTolerance);
        if (debugExpectedTolerance < 0.000001)
        {
            debugExpectedTolerance = 0.05;
        }
        minFaceUvMargin = Math.Max(0.0, Math.Min(minFaceUvMargin, 0.49));
        maxRuntimeSec = Math.Max(10.0, Math.Min(maxRuntimeSec, 1800.0));
        DateTime runtimeStart = DateTime.UtcNow;
        bool stoppedByRuntimeBudget = false;
        minCandidateThickness = Math.Abs(minCandidateThickness);
        if (minCandidateThickness < 0.000001)
        {
            minCandidateThickness = 0.000001;
        }

        string bodyName = "";
        string bodyJournalId = "";
        try { bodyName = body.Name; } catch { }
        try { bodyJournalId = body.JournalIdentifier; } catch { }

        NXOpen.Face[] faces = new NXOpen.Face[0];
        try { faces = body.GetFaces(); } catch { }
        if (faces.Length < 2)
        {
            return "{\"ok\":false,\"error\":\"Target body does not have enough faces to estimate wall thickness.\",\"body_name\":\""
                + JsonEscape(bodyName)
                + "\"}";
        }

        string[] faceTypes = new string[faces.Length];
        bool[] isBlendFace = new bool[faces.Length];
        bool[] isHoleLikeFace = new bool[faces.Length];
        bool[] hasBox = new bool[faces.Length];
        double[][] minBox = new double[faces.Length][];
        double[][] maxBox = new double[faces.Length][];
        int sourceFaceCount = 0;
        int skippedBlendFaceCount = 0;
        int skippedSourceTypeFaceCount = 0;
        int noBoxFaceCount = 0;

        for (int i = 0; i < faces.Length; i++)
        {
            try { faceTypes[i] = faces[i].SolidFaceType.ToString(); } catch { faceTypes[i] = ""; }
            isBlendFace[i] = IsNonWallDetailFaceType(faceTypes[i]);
            isHoleLikeFace[i] = IsHoleLikeFaceType(faceTypes[i])
                || (!stableWallFacesOnly && IsCurvedWallSourceFace(ufSession, faces[i], faceTypes[i]));
            minBox[i] = new double[3];
            maxBox[i] = new double[3];
            hasBox[i] = TryFaceEdgeBoundingBox(faces[i], minBox[i], maxBox[i]);
            if (!hasBox[i])
            {
                noBoxFaceCount++;
            }
        }

        int[] candidateA = new int[maxCandidates];
        int[] candidateB = new int[maxCandidates];
        double[] candidateApprox = new double[maxCandidates];
        double[] candidateDot = new double[maxCandidates];
        int candidateCount = 0;
        int checkedPairCount = 0;
        int skippedTargetBlendPairCount = 0;
        int skippedBoxPairCount = 0;
        int skippedUnstableSurfacePairCount = 0;
        int plannedSourceFaceCount = 0;
        for (int i = 0; i < faces.Length; i++)
        {
            if (skipBlendFaces && isBlendFace[i])
            {
                continue;
            }
            if (stableWallFacesOnly && !IsStableWallFaceType(faceTypes[i]))
            {
                continue;
            }
            if (sourceHoleFacesOnly && !isHoleLikeFace[i])
            {
                continue;
            }
            if (!hasBox[i])
            {
                continue;
            }
            plannedSourceFaceCount++;
        }
        int perSourceCandidateLimit = Math.Max(1, Math.Min(40, maxCandidates / Math.Max(1, plannedSourceFaceCount)));

        for (int i = 0; i < faces.Length; i++)
        {
            if ((DateTime.UtcNow - runtimeStart).TotalSeconds > maxRuntimeSec)
            {
                stoppedByRuntimeBudget = true;
                break;
            }
            if (skipBlendFaces && isBlendFace[i])
            {
                skippedBlendFaceCount++;
                continue;
            }
            if (stableWallFacesOnly && !IsStableWallFaceType(faceTypes[i]))
            {
                skippedUnstableSurfacePairCount++;
                continue;
            }
            if (sourceHoleFacesOnly && !isHoleLikeFace[i])
            {
                skippedSourceTypeFaceCount++;
                continue;
            }
            if (!hasBox[i])
            {
                continue;
            }

            sourceFaceCount++;
            int[] localA = new int[perSourceCandidateLimit];
            int[] localB = new int[perSourceCandidateLimit];
            double[] localApprox = new double[perSourceCandidateLimit];
            double[] localDot = new double[perSourceCandidateLimit];
            int localCount = 0;
            for (int j = 0; j < faces.Length; j++)
            {
                if (i == j)
                {
                    continue;
                }
                if (skipBlendFaces && isBlendFace[j])
                {
                    skippedTargetBlendPairCount++;
                    continue;
                }
                if (stableWallFacesOnly && !IsStableWallFaceType(faceTypes[j]))
                {
                    skippedUnstableSurfacePairCount++;
                    continue;
                }
                if (!hasBox[j])
                {
                    skippedBoxPairCount++;
                    continue;
                }

                checkedPairCount++;
                double aabbDistance = AabbDistance(minBox[i], maxBox[i], minBox[j], maxBox[j]);
                double score = aabbDistance + (BoxCenterDistance(minBox[i], maxBox[i], minBox[j], maxBox[j]) * 0.000001);
                InsertCandidate(localA, localB, localApprox, localDot, ref localCount, perSourceCandidateLimit, i, j, score, aabbDistance);
            }

            for (int localIndex = 0; localIndex < localCount; localIndex++)
            {
                if (candidateCount >= maxCandidates)
                {
                    break;
                }
                candidateA[candidateCount] = localA[localIndex];
                candidateB[candidateCount] = localB[localIndex];
                candidateApprox[candidateCount] = localDot[localIndex];
                candidateDot[candidateCount] = localApprox[localIndex];
                candidateCount++;
            }
        }

        if (candidateCount == 0)
        {
            return "{\"ok\":false,\"error\":\"No AABB candidates found for wall-thickness estimation.\",\"body_name\":\""
                + JsonEscape(bodyName)
                + "\",\"face_count\":"
                + faces.Length
                + ",\"source_face_count\":"
                + sourceFaceCount
                + "}";
        }

        double bestDistance = Double.MaxValue;
        int bestA = -1;
        int bestB = -1;
        double bestApprox = 0.0;
        double[] bestPointA = new double[3];
        double[] bestPointB = new double[3];
        int exactPairCount = 0;
        int skippedTouchingPairCount = 0;
        int skippedFailedPairCount = 0;
        int skippedLowAlignmentPairCount = 0;
        int skippedSameNormalPairCount = 0;
        int skippedOutsideBodyMidpointPairCount = 0;
        double bestAlignmentA = 0.0;
        double bestAlignmentB = 0.0;
        double bestNormalDot = 0.0;
        string bestMeasurementMethod = "exact_face_minimum_distance";
        double normalAlignmentMin = stableWallFacesOnly ? 0.75 : 0.65;
        double normalDotMax = stableWallFacesOnly ? -0.85 : -0.15;
        WallThicknessCandidateReport[] topCandidates = new WallThicknessCandidateReport[reportCandidateCount];
        int topCandidateCount = 0;
        WallThicknessCandidateReport[] expectedCandidates = new WallThicknessCandidateReport[reportCandidateCount];
        int expectedCandidateCount = 0;
        int skippedLowUvMarginPairCount = 0;

        int exactLimit = Math.Min(candidateCount, maxExactPairs);
        for (int candidateIndex = 0; candidateIndex < exactLimit; candidateIndex++)
        {
            if ((DateTime.UtcNow - runtimeStart).TotalSeconds > maxRuntimeSec)
            {
                stoppedByRuntimeBudget = true;
                break;
            }
            double distance = 0.0;
            double[] pointA = new double[3];
            double[] pointB = new double[3];
            try
            {
                exactPairCount++;
                ufSession.Modl.AskMinimumDist(
                    faces[candidateA[candidateIndex]].Tag,
                    faces[candidateB[candidateIndex]].Tag,
                    0,
                    new double[3],
                    0,
                    new double[3],
                    out distance,
                    pointA,
                    pointB
                );
            }
            catch
            {
                skippedFailedPairCount++;
                continue;
            }

            if (distance <= minCandidateThickness)
            {
                skippedTouchingPairCount++;
                continue;
            }

            double[] normalA = new double[3];
            double[] normalB = new double[3];
            double[] direction = new double[]
            {
                pointB[0] - pointA[0],
                pointB[1] - pointA[1],
                pointB[2] - pointA[2]
            };
            double directionLength = Math.Sqrt(direction[0] * direction[0] + direction[1] * direction[1] + direction[2] * direction[2]);
            if (directionLength <= 0.0000001
                || !TryFaceNormalAtPoint(ufSession, faces[candidateA[candidateIndex]], pointA, normalA)
                || !TryFaceNormalAtPoint(ufSession, faces[candidateB[candidateIndex]], pointB, normalB))
            {
                skippedLowAlignmentPairCount++;
                continue;
            }
            direction[0] = direction[0] / directionLength;
            direction[1] = direction[1] / directionLength;
            direction[2] = direction[2] / directionLength;
            double alignmentA = Math.Abs(Dot(direction, normalA));
            double alignmentB = Math.Abs(Dot(direction, normalB));
            double normalDot = Dot(normalA, normalB);
            if (alignmentA < normalAlignmentMin || alignmentB < normalAlignmentMin)
            {
                skippedLowAlignmentPairCount++;
                continue;
            }
            if (normalDot > normalDotMax)
            {
                skippedSameNormalPairCount++;
                continue;
            }
            double[] midpoint = new double[]
            {
                (pointA[0] + pointB[0]) / 2.0,
                (pointA[1] + pointB[1]) / 2.0,
                (pointA[2] + pointB[2]) / 2.0
            };
            int containmentStatus = 0;
            try
            {
                ufSession.Modl.AskPointContainment(midpoint, body.Tag, out containmentStatus);
            }
            catch
            {
                containmentStatus = 0;
            }
            if (containmentStatus != 1)
            {
                skippedOutsideBodyMidpointPairCount++;
                continue;
            }

            double thicknessForRanking = distance;
            string measurementMethod = "exact_face_minimum_distance";
            double cylinderPlaneThickness = 0.0;
            double axisPlaneNormalDot = 0.0;
            if (TryCylinderPlaneWallThickness(
                    ufSession,
                    faces[candidateA[candidateIndex]],
                    faceTypes[candidateA[candidateIndex]],
                    faces[candidateB[candidateIndex]],
                    faceTypes[candidateB[candidateIndex]],
                    out cylinderPlaneThickness,
                    out axisPlaneNormalDot))
            {
                thicknessForRanking = cylinderPlaneThickness;
                measurementMethod = "cylinder_axis_to_plane_minus_radius_section_proxy";
            }

            if (thicknessForRanking <= minCandidateThickness)
            {
                skippedTouchingPairCount++;
                continue;
            }

            double uvMarginA = TryFaceUvInteriorMargin(ufSession, faces[candidateA[candidateIndex]], pointA);
            double uvMarginB = TryFaceUvInteriorMargin(ufSession, faces[candidateB[candidateIndex]], pointB);
            if (minFaceUvMargin > 0.0
                && ((uvMarginA >= 0.0 && uvMarginA < minFaceUvMargin)
                    || (uvMarginB >= 0.0 && uvMarginB < minFaceUvMargin)))
            {
                skippedLowUvMarginPairCount++;
                continue;
            }

            InsertWallReportCandidate(
                topCandidates,
                ref topCandidateCount,
                reportCandidateCount,
                candidateA[candidateIndex],
                candidateB[candidateIndex],
                thicknessForRanking,
                candidateApprox[candidateIndex],
                alignmentA,
                alignmentB,
                normalDot,
                uvMarginA,
                uvMarginB,
                pointA,
                pointB,
                thicknessForRanking
            );

            if (debugExpectedThickness > 0.0 && Math.Abs(thicknessForRanking - debugExpectedThickness) <= debugExpectedTolerance)
            {
                InsertWallReportCandidate(
                    expectedCandidates,
                    ref expectedCandidateCount,
                    reportCandidateCount,
                    candidateA[candidateIndex],
                    candidateB[candidateIndex],
                    thicknessForRanking,
                    candidateApprox[candidateIndex],
                    alignmentA,
                    alignmentB,
                    normalDot,
                    uvMarginA,
                    uvMarginB,
                    pointA,
                    pointB,
                    Math.Abs(thicknessForRanking - debugExpectedThickness)
                );
            }

            if (thicknessForRanking < bestDistance)
            {
                bestDistance = thicknessForRanking;
                bestA = candidateA[candidateIndex];
                bestB = candidateB[candidateIndex];
                bestApprox = candidateApprox[candidateIndex];
                CopyPoint(pointA, bestPointA);
                CopyPoint(pointB, bestPointB);
                bestAlignmentA = alignmentA;
                bestAlignmentB = alignmentB;
                bestNormalDot = normalDot;
                bestMeasurementMethod = measurementMethod;
            }
        }

        if (bestA < 0 || bestB < 0)
        {
            return "{\"ok\":false,\"error\":\"Exact AABB-ranked checks did not find a valid non-touching face pair.\",\"body_name\":\""
                + JsonEscape(bodyName)
                + "\",\"face_count\":"
                + faces.Length
                + ",\"candidate_count\":"
                + candidateCount
                + ",\"exact_pair_count\":"
                + exactPairCount
                + "}";
        }

        bool colored = false;
        string colorError = "";
        try
        {
            NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
                NXOpen.Session.MarkVisibility.Visible,
                "MCP color AABB thinnest wall face pair"
            );
            NXOpen.DisplayModification displayModification = session.DisplayManager.NewDisplayModification();
            displayModification.ApplyToAllFaces = false;
            displayModification.NewColor = colorIndex;
            displayModification.Apply(new NXOpen.DisplayableObject[] { faces[bestA], faces[bestB] });
            displayModification.Dispose();
            try { faces[bestA].SetName("MCP_AABB_MIN_WALL_FACE_A_" + FormatMm(bestDistance)); } catch { }
            try { faces[bestB].SetName("MCP_AABB_MIN_WALL_FACE_B_" + FormatMm(bestDistance)); } catch { }
            session.UpdateManager.DoUpdate(markId);
            try { faces[bestA].RedisplayObject(); } catch { }
            try { faces[bestB].RedisplayObject(); } catch { }
            colored = true;
        }
        catch (Exception ex)
        {
            colorError = ShortError(ex);
        }

        string faceAName = "";
        string faceBName = "";
        string faceAJournalId = "";
        string faceBJournalId = "";
        try { faceAName = faces[bestA].Name; } catch { }
        try { faceBName = faces[bestB].Name; } catch { }
        try { faceAJournalId = faces[bestA].JournalIdentifier; } catch { }
        try { faceBJournalId = faces[bestB].JournalIdentifier; } catch { }

        return "{"
            + "\"ok\":true,"
            + "\"analysis_type\":\"aabb_ranked_wall_thickness\","
            + "\"method\":\"hole_source_face_aabb_ranked_exact_minimum_distance\","
            + "\"note\":\"POC estimate. It ranks nearby face pairs by face edge bounding boxes, then uses NX exact minimum distance on candidates. For cylinder-plane wall candidates it prefers a section-style cylinder-axis-to-plane-minus-radius thickness, which is closer to mechanical wall-thickness intent around holes.\","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\","
            + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
            + "\"body_journal_id\":\"" + JsonEscape(bodyJournalId) + "\","
            + "\"face_count\":" + faces.Length + ","
            + "\"source_hole_faces_only\":" + BoolJson(sourceHoleFacesOnly) + ","
            + "\"stable_wall_faces_only\":" + BoolJson(stableWallFacesOnly) + ","
            + "\"stopped_by_runtime_budget\":" + BoolJson(stoppedByRuntimeBudget) + ","
            + "\"max_runtime_sec\":" + Num(maxRuntimeSec) + ","
            + "\"elapsed_runtime_sec\":" + Num((DateTime.UtcNow - runtimeStart).TotalSeconds) + ","
            + "\"source_face_count\":" + sourceFaceCount + ","
            + "\"planned_source_face_count\":" + plannedSourceFaceCount + ","
            + "\"per_source_candidate_limit\":" + perSourceCandidateLimit + ","
            + "\"no_box_face_count\":" + noBoxFaceCount + ","
            + "\"skipped_blend_face_count\":" + skippedBlendFaceCount + ","
            + "\"skipped_source_type_face_count\":" + skippedSourceTypeFaceCount + ","
            + "\"skipped_target_blend_pair_count\":" + skippedTargetBlendPairCount + ","
            + "\"skipped_unstable_surface_pair_count\":" + skippedUnstableSurfacePairCount + ","
            + "\"skipped_box_pair_count\":" + skippedBoxPairCount + ","
            + "\"checked_pair_count\":" + checkedPairCount + ","
            + "\"candidate_count\":" + candidateCount + ","
            + "\"max_candidates\":" + maxCandidates + ","
            + "\"exact_pair_count\":" + exactPairCount + ","
            + "\"max_exact_pairs\":" + maxExactPairs + ","
            + "\"skipped_touching_pair_count\":" + skippedTouchingPairCount + ","
            + "\"skipped_low_alignment_pair_count\":" + skippedLowAlignmentPairCount + ","
            + "\"skipped_same_normal_pair_count\":" + skippedSameNormalPairCount + ","
            + "\"skipped_outside_body_midpoint_pair_count\":" + skippedOutsideBodyMidpointPairCount + ","
            + "\"skipped_low_uv_margin_pair_count\":" + skippedLowUvMarginPairCount + ","
            + "\"skipped_failed_pair_count\":" + skippedFailedPairCount + ","
            + "\"normal_alignment_min\":" + Num(normalAlignmentMin) + ","
            + "\"normal_dot_max\":" + Num(normalDotMax) + ","
            + "\"min_face_uv_margin\":" + Num(minFaceUvMargin) + ","
            + "\"min_candidate_thickness_mm\":" + Num(minCandidateThickness) + ","
            + "\"debug_expected_thickness_mm\":" + Num(debugExpectedThickness) + ","
            + "\"debug_expected_tolerance_mm\":" + Num(debugExpectedTolerance) + ","
            + "\"selected_measurement_method\":\"" + JsonEscape(bestMeasurementMethod) + "\","
            + "\"min_thickness_mm\":" + Num(bestDistance) + ","
            + "\"aabb_distance_mm\":" + Num(bestApprox) + ","
            + "\"normal_alignment\":{\"face_a\":" + Num(bestAlignmentA) + ",\"face_b\":" + Num(bestAlignmentB) + "},"
            + "\"normal_dot\":" + Num(bestNormalDot) + ","
            + "\"colored\":" + BoolJson(colored) + ","
            + "\"color_index\":" + colorIndex + ","
            + "\"color_error\":\"" + JsonEscape(colorError) + "\","
            + "\"face_a\":{"
            + "\"index\":" + bestA + ","
            + "\"name\":\"" + JsonEscape(faceAName) + "\","
            + "\"journal_id\":\"" + JsonEscape(faceAJournalId) + "\","
            + "\"face_type\":\"" + JsonEscape(faceTypes[bestA]) + "\","
            + "\"nearest_point_mm\":[" + Num(bestPointA[0]) + "," + Num(bestPointA[1]) + "," + Num(bestPointA[2]) + "]"
            + "},"
            + "\"face_b\":{"
            + "\"index\":" + bestB + ","
            + "\"name\":\"" + JsonEscape(faceBName) + "\","
            + "\"journal_id\":\"" + JsonEscape(faceBJournalId) + "\","
            + "\"face_type\":\"" + JsonEscape(faceTypes[bestB]) + "\","
            + "\"nearest_point_mm\":[" + Num(bestPointB[0]) + "," + Num(bestPointB[1]) + "," + Num(bestPointB[2]) + "]"
            + "}"
            + ",\"top_valid_candidates\":"
            + WallCandidateArrayJson(topCandidates, topCandidateCount, faces, faceTypes)
            + ",\"debug_expected_candidates\":"
            + WallCandidateArrayJson(expectedCandidates, expectedCandidateCount, faces, faceTypes)
            + "}";
    }

    private static string RunSectionSliceInNxProcess(
        NXOpen.Session session,
        string targetBodyName,
        double planeX,
        double planeY,
        double planeZ,
        double normalX,
        double normalY,
        double normalZ,
        int samplesPerCurve,
        double minCandidateThickness,
        string outputDir)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = Path.Combine(Environment.CurrentDirectory, "section_images");
        }
        Directory.CreateDirectory(outputDir);

        string pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NxMcpSectionInProcess.dll");
        if (!File.Exists(pluginPath))
        {
            return "{\"ok\":false,\"error\":\"NX section plugin DLL not found.\",\"plugin_path\":\""
                + JsonEscape(pluginPath)
                + "\"}";
        }

        string responsePath = Path.Combine(
            outputDir,
            "section_slice_response_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json"
        );

        object[] executeArgs = new object[]
        {
            targetBodyName,
            planeX,
            planeY,
            planeZ,
            normalX,
            normalY,
            normalZ,
            samplesPerCurve,
            minCandidateThickness,
            outputDir,
            responsePath
        };

        Exception executeException = null;
        bool executeFinished = false;
        Thread executeThread = new Thread(delegate()
        {
            try
            {
                session.Execute(pluginPath, "NxMcpSectionInProcess", "ExecuteMe", executeArgs);
            }
            catch (Exception ex)
            {
                executeException = ex;
            }
            finally
            {
                executeFinished = true;
            }
        });
        executeThread.IsBackground = true;
        executeThread.Name = "NxMcpSectionExecute";
        executeThread.Start();

        int responseTimeoutMs = 600000;
        try
        {
            string timeoutValue = Environment.GetEnvironmentVariable("NX_MCP_SECTION_RESPONSE_TIMEOUT_MS");
            if (!string.IsNullOrWhiteSpace(timeoutValue))
            {
                responseTimeoutMs = Math.Max(5000, Int32.Parse(timeoutValue, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch
        {
        }

        DateTime waitStart = DateTime.UtcNow;
        while ((DateTime.UtcNow - waitStart).TotalMilliseconds < responseTimeoutMs)
        {
            if (File.Exists(responsePath))
            {
                return File.ReadAllText(responsePath, Encoding.UTF8);
            }
            if (executeFinished)
            {
                break;
            }
            Thread.Sleep(250);
        }

        if (File.Exists(responsePath))
        {
            return File.ReadAllText(responsePath, Encoding.UTF8);
        }

        if (executeException != null)
        {
            return "{\"ok\":false,\"error\":\"NX in-process section execution failed: "
                + JsonEscape(SafeExceptionMessage(executeException))
                + "\",\"plugin_path\":\""
                + JsonEscape(pluginPath)
                + "\"}";
        }

        return "{\"ok\":false,\"error\":\"NX in-process section execution timed out waiting for response file.\",\"response_path\":\""
            + JsonEscape(responsePath)
            + "\",\"timeout_ms\":" + responseTimeoutMs + "}";
    }

    private static string CreateSectionSliceReport(
        NXOpen.Session session,
        NXOpen.UF.UFSession ufSession,
        string targetBodyName,
        double planeX,
        double planeY,
        double planeZ,
        double normalX,
        double normalY,
        double normalZ,
        int samplesPerCurve,
        double minCandidateThickness,
        string outputDir)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        NXOpen.Body body = FindTargetBody(workPart, targetBodyName);
        if (body == null)
        {
            return "{\"ok\":false,\"error\":\"Body not found. Provide target_body_name from nx_remoting_list_bodies.\",\"target_body_name\":\""
                + JsonEscape(targetBodyName)
                + "\"}";
        }

        double[] origin = new double[] { planeX, planeY, planeZ };
        double[] normal = new double[] { normalX, normalY, normalZ };
        if (!Normalize(normal))
        {
            return "{\"ok\":false,\"error\":\"Invalid section plane normal.\"}";
        }

        double[] basisU = new double[3];
        double[] basisV = new double[3];
        SectionBasis(normal, basisU, basisV);

        samplesPerCurve = Math.Max(8, Math.Min(samplesPerCurve, 200));
        minCandidateThickness = Math.Max(0.0, minCandidateThickness);
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = Path.Combine(Environment.CurrentDirectory, "section_images");
        }
        Directory.CreateDirectory(outputDir);

        string bodyName = "";
        string bodyJournalId = "";
        try { bodyName = body.Name; } catch { }
        try { bodyJournalId = body.JournalIdentifier; } catch { }

        return CreateFacetedSectionSliceReport(
            session,
            ufSession,
            workPart,
            body,
            bodyName,
            bodyJournalId,
            origin,
            normal,
            basisU,
            basisV,
            planeX,
            planeY,
            planeZ,
            samplesPerCurve,
            minCandidateThickness,
            outputDir
        );

        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP create section slice report"
        );

        NXOpen.Plane sectionPlane = null;
        NXOpen.Tag sectionTag = NXOpen.Tag.Null;
        NXOpen.Tag[] curveTags = new NXOpen.Tag[0];
        int curveCount = 0;
        string sectionError = "";

        try
        {
            sectionPlane = workPart.Planes.CreatePlane(
                new NXOpen.Point3d(planeX, planeY, planeZ),
                new NXOpen.Vector3d(normal[0], normal[1], normal[2]),
                NXOpen.SmartObject.UpdateOption.WithinModeling
            );

            NXOpen.UF.UFCurve.SectionGeneralData generalData = new NXOpen.UF.UFCurve.SectionGeneralData();
            generalData.objects = new NXOpen.Tag[] { body.Tag };
            generalData.num_objects = 1;
            generalData.associate = 1;
            generalData.grouping = 0;
            generalData.join_type = 0;
            generalData.tolerance = 0.01;
            NXOpen.UF.UFModl.CurveFitData curveFitData = new NXOpen.UF.UFModl.CurveFitData();
            curveFitData.curve_fit_method = 0;
            curveFitData.maximum_degree = 3;
            curveFitData.maximum_segments = 200;
            generalData.curve_fit_data = curveFitData;

            NXOpen.UF.UFCurve.SectionPlanesData planesData = new NXOpen.UF.UFCurve.SectionPlanesData();
            planesData.planes = new NXOpen.Tag[] { sectionPlane.Tag };
            planesData.num_planes = 1;

            ufSession.Curve.SectionFromPlanes(ref generalData, ref planesData, out sectionTag);
            try
            {
                NXOpen.TaggedObject sectionObject = NXOpen.Utilities.NXObjectManager.Get(sectionTag);
                NXOpen.NXObject sectionNxObject = sectionObject as NXOpen.NXObject;
                if (sectionNxObject != null)
                {
                    sectionNxObject.SetName("MCP_SECTION_Y_" + planeY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch
            {
            }

            try
            {
                ufSession.Curve.AskFeatureCurves(sectionTag, out curveCount, out curveTags);
            }
            catch
            {
                NXOpen.Features.Feature sectionFeature = NXOpen.Utilities.NXObjectManager.Get(sectionTag) as NXOpen.Features.Feature;
                if (sectionFeature != null)
                {
                    NXOpen.NXObject[] entities = sectionFeature.GetEntities();
                    List<NXOpen.Tag> tags = new List<NXOpen.Tag>();
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (entities[i] is NXOpen.Curve)
                        {
                            tags.Add(entities[i].Tag);
                        }
                    }
                    curveTags = tags.ToArray();
                    curveCount = curveTags.Length;
                }
            }
        }
        catch (Exception ex)
        {
            sectionError = ShortError(ex);
        }

        if (!string.IsNullOrWhiteSpace(sectionError))
        {
            return "{\"ok\":false,\"error\":\"Failed to create section curves: "
                + JsonEscape(sectionError)
                + "\"}";
        }

        if (curveTags == null || curveTags.Length == 0)
        {
            return "{\"ok\":false,\"error\":\"Section plane did not produce any section curves.\",\"body_journal_id\":\""
                + JsonEscape(bodyJournalId)
                + "\",\"plane_point_mm\":["
                + Num(planeX) + "," + Num(planeY) + "," + Num(planeZ)
                + "],\"plane_normal\":["
                + Num(normal[0]) + "," + Num(normal[1]) + "," + Num(normal[2])
                + "]}";
        }

        List<List<SectionSamplePoint>> polylines = new List<List<SectionSamplePoint>>();
        List<SectionSamplePoint> allPoints = new List<SectionSamplePoint>();
        int failedCurveCount = 0;
        for (int curveIndex = 0; curveIndex < curveTags.Length; curveIndex++)
        {
            List<SectionSamplePoint> points = SampleSectionCurve(
                ufSession,
                curveTags[curveIndex],
                curveIndex,
                samplesPerCurve,
                origin,
                basisU,
                basisV
            );
            if (points.Count == 0)
            {
                failedCurveCount++;
                continue;
            }
            EstimateSectionTangents(points);
            polylines.Add(points);
            for (int i = 0; i < points.Count; i++)
            {
                allPoints.Add(points[i]);
            }
        }

        if (allPoints.Count < 2)
        {
            return "{\"ok\":false,\"error\":\"Section curves were created but could not be sampled.\",\"curve_count\":"
                + curveTags.Length
                + ",\"failed_curve_count\":"
                + failedCurveCount
                + "}";
        }

        SectionThicknessResult rawResult = FindSectionThickness(
            ufSession,
            body,
            allPoints,
            minCandidateThickness,
            false,
            true
        );
        SectionThicknessResult wallResult = FindSectionThickness(
            ufSession,
            body,
            allPoints,
            minCandidateThickness,
            true,
            true
        );
        SectionThicknessResult selectedResult = wallResult.Found ? wallResult : rawResult;

        double minU = Double.MaxValue;
        double minV = Double.MaxValue;
        double maxU = -Double.MaxValue;
        double maxV = -Double.MaxValue;
        for (int i = 0; i < allPoints.Count; i++)
        {
            if (allPoints[i].U < minU) { minU = allPoints[i].U; }
            if (allPoints[i].V < minV) { minV = allPoints[i].V; }
            if (allPoints[i].U > maxU) { maxU = allPoints[i].U; }
            if (allPoints[i].V > maxV) { maxV = allPoints[i].V; }
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string svgPath = Path.Combine(outputDir, "section_y_" + planeY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "p") + "_" + timestamp + ".svg");
        WriteSectionSvg(svgPath, polylines, selectedResult, minU, minV, maxU, maxV, planeY);

        try
        {
            session.UpdateManager.DoUpdate(markId);
        }
        catch
        {
        }

        return "{"
            + "\"ok\":true,"
            + "\"analysis_type\":\"section_slice_wall_thickness\","
            + "\"method\":\"uf_section_curves_sampled_2d_boundary_distance\","
            + "\"note\":\"Creates NX section curves at the requested plane, samples them into the section coordinate system, then reports raw and tangent-filtered inside-solid boundary distances. This is a first section-based POC; dimensions should be verified against NX drafting/measure for release use.\","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\","
            + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
            + "\"body_journal_id\":\"" + JsonEscape(bodyJournalId) + "\","
            + "\"plane_point_mm\":[" + Num(planeX) + "," + Num(planeY) + "," + Num(planeZ) + "],"
            + "\"plane_normal\":[" + Num(normal[0]) + "," + Num(normal[1]) + "," + Num(normal[2]) + "],"
            + "\"section_feature_tag\":\"" + JsonEscape(sectionTag.ToString()) + "\","
            + "\"curve_count\":" + curveTags.Length + ","
            + "\"sampled_curve_count\":" + polylines.Count + ","
            + "\"failed_curve_count\":" + failedCurveCount + ","
            + "\"sample_point_count\":" + allPoints.Count + ","
            + "\"samples_per_curve\":" + samplesPerCurve + ","
            + "\"min_candidate_thickness_mm\":" + Num(minCandidateThickness) + ","
            + "\"raw_min_thickness\":" + SectionThicknessJson(rawResult) + ","
            + "\"wall_min_thickness\":" + SectionThicknessJson(wallResult) + ","
            + "\"selected_min_thickness\":" + SectionThicknessJson(selectedResult) + ","
            + "\"section_bounds_2d_mm\":{\"min_u\":" + Num(minU) + ",\"min_v\":" + Num(minV) + ",\"max_u\":" + Num(maxU) + ",\"max_v\":" + Num(maxV) + "},"
            + "\"image_path\":\"" + JsonEscape(svgPath) + "\""
            + "}";
    }

    private static void SectionBasis(double[] normal, double[] basisU, double[] basisV)
    {
        if (Math.Abs(normal[0]) < 0.000001 && Math.Abs(Math.Abs(normal[1]) - 1.0) < 0.000001 && Math.Abs(normal[2]) < 0.000001)
        {
            basisU[0] = 1.0;
            basisU[1] = 0.0;
            basisU[2] = 0.0;
            basisV[0] = 0.0;
            basisV[1] = 0.0;
            basisV[2] = normal[1] >= 0.0 ? 1.0 : -1.0;
            return;
        }

        double[] reference = Math.Abs(normal[2]) < 0.9
            ? new double[] { 0.0, 0.0, 1.0 }
            : new double[] { 1.0, 0.0, 0.0 };
        Cross(reference, normal, basisU);
        Normalize(basisU);
        Cross(normal, basisU, basisV);
        Normalize(basisV);
    }

    private static string CreateFacetedSectionSliceReport(
        NXOpen.Session session,
        NXOpen.UF.UFSession ufSession,
        NXOpen.Part workPart,
        NXOpen.Body body,
        string bodyName,
        string bodyJournalId,
        double[] origin,
        double[] normal,
        double[] basisU,
        double[] basisV,
        double planeX,
        double planeY,
        double planeZ,
        int samplesPerCurve,
        double minCandidateThickness,
        string outputDir)
    {
        NXOpen.Tag facetModel = NXOpen.Tag.Null;
        int facetCount = 0;
        int intersectedFacetCount = 0;
        List<List<SectionSamplePoint>> polylines = new List<List<SectionSamplePoint>>();
        List<SectionSamplePoint> allPoints = new List<SectionSamplePoint>();
        string facetError = "";

        try
        {
            NXOpen.UF.UFFacet.Parameters parameters = new NXOpen.UF.UFFacet.Parameters();
            ufSession.Facet.AskDefaultParameters(out parameters);
            parameters.max_facet_edges = 3;
            parameters.specify_surface_tolerance = true;
            parameters.surface_dist_tolerance = 0.06;
            parameters.surface_angular_tolerance = 0.15;
            parameters.specify_curve_tolerance = true;
            parameters.curve_dist_tolerance = 0.06;
            parameters.curve_angular_tolerance = 0.15;
            parameters.specify_max_facet_size = true;
            parameters.max_facet_size = 1.2;
            parameters.store_face_tags = true;

            ufSession.Facet.FacetSolid(body.Tag, ref parameters, out facetModel);
            int facetId = 0;
            int segmentIndex = 0;
            while (true)
            {
                ufSession.Facet.CycleFacets(facetModel, ref facetId);
                if (facetId == 0)
                {
                    break;
                }
                facetCount++;

                int vertexCount = 0;
                double[,] vertices = new double[3, 3];
                try
                {
                    ufSession.Facet.AskVerticesOfFacet(facetModel, facetId, out vertexCount, vertices);
                }
                catch
                {
                    continue;
                }
                if (vertexCount < 3 || vertices == null)
                {
                    continue;
                }

                double[][] polygon = new double[vertexCount][];
                for (int i = 0; i < vertexCount; i++)
                {
                    polygon[i] = FacetVertex(vertices, i, vertexCount);
                }

                List<double[]> intersections = IntersectPolygonWithPlane(polygon, origin, normal, 0.000001);
                if (intersections.Count < 2)
                {
                    continue;
                }

                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    List<SectionSamplePoint> segment = SampleSectionSegment(
                        intersections[i],
                        intersections[i + 1],
                        segmentIndex,
                        origin,
                        basisU,
                        basisV,
                        samplesPerCurve
                    );
                    if (segment.Count < 2)
                    {
                        continue;
                    }
                    EstimateSectionTangents(segment);
                    polylines.Add(segment);
                    for (int j = 0; j < segment.Count; j++)
                    {
                        allPoints.Add(segment[j]);
                    }
                    segmentIndex++;
                    intersectedFacetCount++;
                }
            }
        }
        catch (Exception ex)
        {
            facetError = ShortError(ex);
        }

        if (!string.IsNullOrWhiteSpace(facetError))
        {
            return "{\"ok\":false,\"error\":\"Failed to create faceted section: "
                + JsonEscape(facetError)
                + "\"}";
        }

        if (allPoints.Count < 2)
        {
            return "{\"ok\":false,\"error\":\"The requested plane did not intersect the faceted body.\",\"facet_count\":"
                + facetCount
                + ",\"body_journal_id\":\""
                + JsonEscape(bodyJournalId)
                + "\"}";
        }

        if (allPoints.Count > 4500)
        {
            int stride = (int)Math.Ceiling((double)allPoints.Count / 4500.0);
            List<SectionSamplePoint> reduced = new List<SectionSamplePoint>();
            for (int i = 0; i < allPoints.Count; i += stride)
            {
                reduced.Add(allPoints[i]);
            }
            allPoints = reduced;
        }

        SectionThicknessResult rawResult = FindSectionThickness(
            ufSession,
            body,
            allPoints,
            minCandidateThickness,
            false,
            false
        );
        SectionThicknessResult wallResult = FindSectionThickness(
            ufSession,
            body,
            allPoints,
            minCandidateThickness,
            true,
            false
        );
        SectionThicknessResult selectedResult = wallResult.Found ? wallResult : rawResult;

        double minU = Double.MaxValue;
        double minV = Double.MaxValue;
        double maxU = -Double.MaxValue;
        double maxV = -Double.MaxValue;
        for (int i = 0; i < allPoints.Count; i++)
        {
            if (allPoints[i].U < minU) { minU = allPoints[i].U; }
            if (allPoints[i].V < minV) { minV = allPoints[i].V; }
            if (allPoints[i].U > maxU) { maxU = allPoints[i].U; }
            if (allPoints[i].V > maxV) { maxV = allPoints[i].V; }
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string svgPath = Path.Combine(outputDir, "faceted_section_y_" + planeY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "p") + "_" + timestamp + ".svg");
        WriteSectionSvg(svgPath, polylines, selectedResult, minU, minV, maxU, maxV, planeY);

        return "{"
            + "\"ok\":true,"
            + "\"analysis_type\":\"faceted_section_slice_wall_thickness\","
            + "\"method\":\"uf_facet_solid_plane_intersection_sampled_2d_boundary_distance\","
            + "\"note\":\"Uses a faceted approximation of the NX body, intersects facets with the requested section plane, exports an SVG section image, and estimates 2D wall thickness. This avoids the remoting crash seen with UF section curve creation, but final values should be verified with exact NX section/measure.\","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\","
            + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
            + "\"body_journal_id\":\"" + JsonEscape(bodyJournalId) + "\","
            + "\"plane_point_mm\":[" + Num(planeX) + "," + Num(planeY) + "," + Num(planeZ) + "],"
            + "\"plane_normal\":[" + Num(normal[0]) + "," + Num(normal[1]) + "," + Num(normal[2]) + "],"
            + "\"facet_model_tag\":\"" + JsonEscape(facetModel.ToString()) + "\","
            + "\"facet_count\":" + facetCount + ","
            + "\"intersected_facet_segment_count\":" + intersectedFacetCount + ","
            + "\"sample_point_count\":" + allPoints.Count + ","
            + "\"samples_per_segment\":" + samplesPerCurve + ","
            + "\"facet_surface_dist_tolerance_mm\":0.06,"
            + "\"min_candidate_thickness_mm\":" + Num(minCandidateThickness) + ","
            + "\"raw_min_thickness\":" + SectionThicknessJson(rawResult) + ","
            + "\"wall_min_thickness\":" + SectionThicknessJson(wallResult) + ","
            + "\"selected_min_thickness\":" + SectionThicknessJson(selectedResult) + ","
            + "\"section_bounds_2d_mm\":{\"min_u\":" + Num(minU) + ",\"min_v\":" + Num(minV) + ",\"max_u\":" + Num(maxU) + ",\"max_v\":" + Num(maxV) + "},"
            + "\"image_path\":\"" + JsonEscape(svgPath) + "\""
            + "}";
    }

    private static double[] FacetVertex(double[,] vertices, int index, int vertexCount)
    {
        int dim0 = vertices.GetLength(0);
        int dim1 = vertices.GetLength(1);
        if (dim0 >= vertexCount && dim1 >= 3)
        {
            return new double[] { vertices[index, 0], vertices[index, 1], vertices[index, 2] };
        }
        return new double[] { vertices[0, index], vertices[1, index], vertices[2, index] };
    }

    private static List<double[]> IntersectPolygonWithPlane(double[][] polygon, double[] origin, double[] normal, double tolerance)
    {
        List<double[]> intersections = new List<double[]>();
        int count = polygon.Length;
        for (int i = 0; i < count; i++)
        {
            double[] a = polygon[i];
            double[] b = polygon[(i + 1) % count];
            double da = SignedDistanceToPlane(a, origin, normal);
            double db = SignedDistanceToPlane(b, origin, normal);
            if (Math.Abs(da) <= tolerance)
            {
                AddUniquePoint(intersections, a, tolerance * 10.0);
            }
            if ((da > tolerance && db < -tolerance) || (da < -tolerance && db > tolerance))
            {
                double t = da / (da - db);
                double[] p = new double[]
                {
                    a[0] + ((b[0] - a[0]) * t),
                    a[1] + ((b[1] - a[1]) * t),
                    a[2] + ((b[2] - a[2]) * t)
                };
                AddUniquePoint(intersections, p, tolerance * 10.0);
            }
        }
        return intersections;
    }

    private static List<SectionSamplePoint> SampleSectionSegment(
        double[] a,
        double[] b,
        int segmentIndex,
        double[] origin,
        double[] basisU,
        double[] basisV,
        int samplesPerSegment)
    {
        List<SectionSamplePoint> points = new List<SectionSamplePoint>();
        double dx = b[0] - a[0];
        double dy = b[1] - a[1];
        double dz = b[2] - a[2];
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int count = Math.Max(2, Math.Min(samplesPerSegment, (int)Math.Ceiling(length / 0.08) + 1));
        for (int i = 0; i < count; i++)
        {
            double t = count <= 1 ? 0.0 : (double)i / (double)(count - 1);
            SectionSamplePoint point = new SectionSamplePoint();
            point.CurveIndex = segmentIndex;
            point.PointIndex = i;
            point.X = a[0] + dx * t;
            point.Y = a[1] + dy * t;
            point.Z = a[2] + dz * t;
            double[] relative = new double[]
            {
                point.X - origin[0],
                point.Y - origin[1],
                point.Z - origin[2]
            };
            point.U = Dot(relative, basisU);
            point.V = Dot(relative, basisV);
            point.TangentU = Dot(new double[] { dx, dy, dz }, basisU);
            point.TangentV = Dot(new double[] { dx, dy, dz }, basisV);
            Normalize2D(ref point.TangentU, ref point.TangentV);
            points.Add(point);
        }
        return points;
    }

    private static double SignedDistanceToPlane(double[] point, double[] origin, double[] normal)
    {
        return ((point[0] - origin[0]) * normal[0])
            + ((point[1] - origin[1]) * normal[1])
            + ((point[2] - origin[2]) * normal[2]);
    }

    private static void AddUniquePoint(List<double[]> points, double[] candidate, double tolerance)
    {
        double toleranceSquared = tolerance * tolerance;
        for (int i = 0; i < points.Count; i++)
        {
            double dx = points[i][0] - candidate[0];
            double dy = points[i][1] - candidate[1];
            double dz = points[i][2] - candidate[2];
            if ((dx * dx + dy * dy + dz * dz) <= toleranceSquared)
            {
                return;
            }
        }
        points.Add(new double[] { candidate[0], candidate[1], candidate[2] });
    }

    private static List<SectionSamplePoint> SampleSectionCurve(
        NXOpen.UF.UFSession ufSession,
        NXOpen.Tag curveTag,
        int curveIndex,
        int samplesPerCurve,
        double[] origin,
        double[] basisU,
        double[] basisV)
    {
        List<SectionSamplePoint> points = new List<SectionSamplePoint>();
        try
        {
            double[] parameterRange = new double[2];
            int periodicity = 0;
            ufSession.Curve.AskParameterization(curveTag, parameterRange, out periodicity);
            double start = parameterRange[0];
            double end = parameterRange[1];
            int count = samplesPerCurve;
            if (Math.Abs(end - start) < 0.0000001)
            {
                count = 1;
            }

            for (int sample = 0; sample < count; sample++)
            {
                double t = count <= 1 ? start : start + ((end - start) * sample / (count - 1));
                double[] posAndDeriv = new double[6];
                try
                {
                    ufSession.Curve.EvaluateCurve(curveTag, t, 1, posAndDeriv);
                }
                catch
                {
                    posAndDeriv = new double[3];
                    ufSession.Curve.EvaluateCurve(curveTag, t, 0, posAndDeriv);
                }

                SectionSamplePoint point = new SectionSamplePoint();
                point.CurveIndex = curveIndex;
                point.PointIndex = sample;
                point.X = posAndDeriv[0];
                point.Y = posAndDeriv[1];
                point.Z = posAndDeriv[2];
                double[] relative = new double[]
                {
                    point.X - origin[0],
                    point.Y - origin[1],
                    point.Z - origin[2]
                };
                point.U = Dot(relative, basisU);
                point.V = Dot(relative, basisV);
                if (posAndDeriv.Length >= 6)
                {
                    double[] tangent3d = new double[] { posAndDeriv[3], posAndDeriv[4], posAndDeriv[5] };
                    point.TangentU = Dot(tangent3d, basisU);
                    point.TangentV = Dot(tangent3d, basisV);
                    Normalize2D(ref point.TangentU, ref point.TangentV);
                }
                points.Add(point);
            }
        }
        catch
        {
        }

        return points;
    }

    private static void EstimateSectionTangents(List<SectionSamplePoint> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        for (int i = 0; i < points.Count; i++)
        {
            if (Math.Abs(points[i].TangentU) > 0.0000001 || Math.Abs(points[i].TangentV) > 0.0000001)
            {
                continue;
            }
            int prev = Math.Max(0, i - 1);
            int next = Math.Min(points.Count - 1, i + 1);
            double tangentU = points[next].U - points[prev].U;
            double tangentV = points[next].V - points[prev].V;
            if (Normalize2D(ref tangentU, ref tangentV))
            {
                points[i].TangentU = tangentU;
                points[i].TangentV = tangentV;
            }
        }
    }

    private static SectionThicknessResult FindSectionThickness(
        NXOpen.UF.UFSession ufSession,
        NXOpen.Body body,
        List<SectionSamplePoint> points,
        double minCandidateThickness,
        bool requireTangentNormal,
        bool checkContainment)
    {
        SectionThicknessResult result = new SectionThicknessResult();
        result.Method = requireTangentNormal
            ? "inside_solid_boundary_distance_with_tangent_normal_filter"
            : "inside_solid_boundary_distance";
        if (!checkContainment)
        {
            result.Method = requireTangentNormal
                ? "2d_boundary_distance_with_tangent_normal_filter_no_containment"
                : "2d_boundary_distance_no_containment";
        }
        double best = 50.0;
        double bestSquared = best * best;
        double tangentDotMax = 0.35;

        for (int i = 0; i < points.Count; i++)
        {
            SectionSamplePoint a = points[i];
            for (int j = i + 1; j < points.Count; j++)
            {
                SectionSamplePoint b = points[j];
                if (a.CurveIndex == b.CurveIndex)
                {
                    continue;
                }

                double du = b.U - a.U;
                double dv = b.V - a.V;
                double d2 = du * du + dv * dv;
                if (d2 <= minCandidateThickness * minCandidateThickness || d2 >= bestSquared)
                {
                    continue;
                }

                double distance = Math.Sqrt(d2);
                double dirU = du / distance;
                double dirV = dv / distance;

                if (requireTangentNormal)
                {
                    double tangentA = Math.Abs((dirU * a.TangentU) + (dirV * a.TangentV));
                    double tangentB = Math.Abs((dirU * b.TangentU) + (dirV * b.TangentV));
                    if (tangentA > tangentDotMax || tangentB > tangentDotMax)
                    {
                        continue;
                    }
                }

                if (checkContainment)
                {
                    double[] midpoint = new double[]
                    {
                        (a.X + b.X) / 2.0,
                        (a.Y + b.Y) / 2.0,
                        (a.Z + b.Z) / 2.0
                    };
                    int containmentStatus = 0;
                    try
                    {
                        ufSession.Modl.AskPointContainment(midpoint, body.Tag, out containmentStatus);
                    }
                    catch
                    {
                        containmentStatus = 0;
                    }
                    if (containmentStatus != 1)
                    {
                        continue;
                    }
                }

                result.Found = true;
                result.Distance = distance;
                result.A = a;
                result.B = b;
                best = distance;
                bestSquared = d2;
            }
        }

        return result;
    }

    private static string SectionThicknessJson(SectionThicknessResult result)
    {
        if (result == null || !result.Found)
        {
            return "{\"found\":false}";
        }

        return "{"
            + "\"found\":true,"
            + "\"method\":\"" + JsonEscape(result.Method) + "\","
            + "\"thickness_mm\":" + Num(result.Distance) + ","
            + "\"point_a\":" + SectionPointJson(result.A) + ","
            + "\"point_b\":" + SectionPointJson(result.B)
            + "}";
    }

    private static string SectionPointJson(SectionSamplePoint point)
    {
        if (point == null)
        {
            return "null";
        }

        return "{"
            + "\"curve_index\":" + point.CurveIndex + ","
            + "\"point_index\":" + point.PointIndex + ","
            + "\"xyz_mm\":[" + Num(point.X) + "," + Num(point.Y) + "," + Num(point.Z) + "],"
            + "\"section_uv_mm\":[" + Num(point.U) + "," + Num(point.V) + "]"
            + "}";
    }

    private static void WriteSectionSvg(
        string svgPath,
        List<List<SectionSamplePoint>> polylines,
        SectionThicknessResult thickness,
        double minU,
        double minV,
        double maxU,
        double maxV,
        double planeY)
    {
        int width = 1400;
        int height = 900;
        double margin = 70.0;
        double spanU = Math.Max(maxU - minU, 1.0);
        double spanV = Math.Max(maxV - minV, 1.0);
        double scale = Math.Min((width - margin * 2.0) / spanU, (height - margin * 2.0) / spanV);

        Func<double, double> sx = delegate(double u) { return margin + ((u - minU) * scale); };
        Func<double, double> sy = delegate(double v) { return height - margin - ((v - minV) * scale); };

        StringBuilder sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width).Append("\" height=\"").Append(height).Append("\" viewBox=\"0 0 ").Append(width).Append(" ").Append(height).Append("\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        sb.Append("<text x=\"30\" y=\"34\" font-family=\"Arial, sans-serif\" font-size=\"22\" fill=\"#111\">NX section slice, plane Y=");
        sb.Append(XmlEscape(Num(planeY))).Append(" mm</text>");
        sb.Append("<text x=\"30\" y=\"60\" font-family=\"Arial, sans-serif\" font-size=\"14\" fill=\"#555\">View coordinates: horizontal X, vertical Z. Red line marks selected minimum section thickness candidate.</text>");

        for (int i = 0; i < polylines.Count; i++)
        {
            if (polylines[i].Count < 2)
            {
                continue;
            }
            sb.Append("<polyline fill=\"none\" stroke=\"#222\" stroke-width=\"1.2\" points=\"");
            for (int j = 0; j < polylines[i].Count; j++)
            {
                if (j > 0)
                {
                    sb.Append(" ");
                }
                sb.Append(Num(sx(polylines[i][j].U))).Append(",").Append(Num(sy(polylines[i][j].V)));
            }
            sb.Append("\"/>");
        }

        if (thickness != null && thickness.Found)
        {
            double x1 = sx(thickness.A.U);
            double y1 = sy(thickness.A.V);
            double x2 = sx(thickness.B.U);
            double y2 = sy(thickness.B.V);
            double labelX = (x1 + x2) / 2.0 + 10.0;
            double labelY = (y1 + y2) / 2.0 - 10.0;
            sb.Append("<line x1=\"").Append(Num(x1)).Append("\" y1=\"").Append(Num(y1)).Append("\" x2=\"").Append(Num(x2)).Append("\" y2=\"").Append(Num(y2)).Append("\" stroke=\"#d71920\" stroke-width=\"3\"/>");
            sb.Append("<circle cx=\"").Append(Num(x1)).Append("\" cy=\"").Append(Num(y1)).Append("\" r=\"4\" fill=\"#d71920\"/>");
            sb.Append("<circle cx=\"").Append(Num(x2)).Append("\" cy=\"").Append(Num(y2)).Append("\" r=\"4\" fill=\"#d71920\"/>");
            sb.Append("<rect x=\"").Append(Num(labelX - 4)).Append("\" y=\"").Append(Num(labelY - 18)).Append("\" width=\"170\" height=\"25\" fill=\"#ffffff\" stroke=\"#d71920\" stroke-width=\"1\"/>");
            sb.Append("<text x=\"").Append(Num(labelX)).Append("\" y=\"").Append(Num(labelY)).Append("\" font-family=\"Arial, sans-serif\" font-size=\"16\" fill=\"#d71920\">min ");
            sb.Append(XmlEscape(Num(thickness.Distance))).Append(" mm</text>");
        }

        sb.Append("<rect x=\"").Append(Num(margin)).Append("\" y=\"").Append(Num(margin)).Append("\" width=\"").Append(Num(spanU * scale)).Append("\" height=\"").Append(Num(spanV * scale)).Append("\" fill=\"none\" stroke=\"#ddd\" stroke-width=\"1\"/>");
        sb.Append("</svg>");
        File.WriteAllText(svgPath, sb.ToString(), Encoding.UTF8);
    }

    private static string ListFeatures(NXOpen.Session session, int limit)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        limit = Math.Max(1, Math.Min(limit, 200));
        NXOpen.Features.Feature[] features = workPart.Features.ToArray();
        int start = Math.Max(0, features.Length - limit);
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"ok\":true,");
        sb.Append("\"work_part_name\":\"").Append(JsonEscape(workPart.Name)).Append("\",");
        sb.Append("\"feature_count\":").Append(features.Length).Append(",");
        sb.Append("\"returned_count\":").Append(features.Length - start).Append(",");
        sb.Append("\"features\":[");
        for (int i = start; i < features.Length; i++)
        {
            if (i > start)
            {
                sb.Append(",");
            }
            NXOpen.Features.Feature feature = features[i];
            string name = "";
            string journalId = "";
            string featureType = "";
            try { name = feature.Name; } catch { }
            try { journalId = feature.JournalIdentifier; } catch { }
            try { featureType = feature.FeatureType; } catch { }

            sb.Append("{");
            sb.Append("\"index\":").Append(i).Append(",");
            sb.Append("\"name\":\"").Append(JsonEscape(name)).Append("\",");
            sb.Append("\"feature_type\":\"").Append(JsonEscape(featureType)).Append("\",");
            sb.Append("\"journal_id\":\"").Append(JsonEscape(journalId)).Append("\"");
            sb.Append("}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string CreateBasicSketch(NXOpen.Session session, string sketchName, double widthMm, double heightMm)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string safeName = string.IsNullOrWhiteSpace(sketchName) ? "MCP Session Sketch" : sketchName;
        if (safeName.Length > 60)
        {
            safeName = safeName.Substring(0, 60);
        }

        widthMm = Math.Max(widthMm, 1.0);
        heightMm = Math.Max(heightMm, 1.0);
        double halfW = widthMm / 2.0;
        double halfH = heightMm / 2.0;

        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP session remoting create basic sketch"
        );

        NXOpen.Point3d origin = new NXOpen.Point3d(0.0, 0.0, 0.0);
        NXOpen.Vector3d normal = new NXOpen.Vector3d(0.0, 0.0, 1.0);
        NXOpen.Plane plane = workPart.Planes.CreatePlane(origin, normal, NXOpen.SmartObject.UpdateOption.WithinModeling);

        NXOpen.SketchInPlaceBuilder sketchBuilder = workPart.Sketches.CreateSketchInPlaceBuilder2(null);
        sketchBuilder.PlaneReference = plane;
        try
        {
            sketchBuilder.PlaneOption = NXOpen.Sketch.PlaneOption.ExistingPlane;
        }
        catch
        {
        }

        NXOpen.NXObject nxObject = sketchBuilder.Commit();
        sketchBuilder.Destroy();

        NXOpen.Sketch sketch = (NXOpen.Sketch)nxObject;
        try
        {
            sketch.SetName(safeName);
        }
        catch
        {
        }

        sketch.Activate(NXOpen.Sketch.ViewReorient.False);

        NXOpen.Point3d p1 = new NXOpen.Point3d(-halfW, -halfH, 0.0);
        NXOpen.Point3d p2 = new NXOpen.Point3d(halfW, -halfH, 0.0);
        NXOpen.Point3d p3 = new NXOpen.Point3d(halfW, halfH, 0.0);
        NXOpen.Point3d p4 = new NXOpen.Point3d(-halfW, halfH, 0.0);

        NXOpen.Line line1 = workPart.Curves.CreateLine(p1, p2);
        NXOpen.Line line2 = workPart.Curves.CreateLine(p2, p3);
        NXOpen.Line line3 = workPart.Curves.CreateLine(p3, p4);
        NXOpen.Line line4 = workPart.Curves.CreateLine(p4, p1);

        session.ActiveSketch.AddGeometry(line1);
        session.ActiveSketch.AddGeometry(line2);
        session.ActiveSketch.AddGeometry(line3);
        session.ActiveSketch.AddGeometry(line4);

        sketch.Update();
        session.ActiveSketch.Deactivate(NXOpen.Sketch.ViewReorient.False, NXOpen.Sketch.UpdateLevel.Model);
        session.UpdateManager.DoUpdate(markId);

        return "{"
            + "\"ok\":true,"
            + "\"created\":true,"
            + "\"sketch_name\":\"" + JsonEscape(safeName) + "\","
            + "\"width_mm\":" + widthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"height_mm\":" + heightMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
            + "}";
    }

    private static string CreateRectangleCurves(NXOpen.Session session, string curveSetName, double widthMm, double heightMm)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string safeName = string.IsNullOrWhiteSpace(curveSetName) ? "MCP Rectangle Curves" : curveSetName;
        if (safeName.Length > 60)
        {
            safeName = safeName.Substring(0, 60);
        }

        widthMm = Math.Max(widthMm, 1.0);
        heightMm = Math.Max(heightMm, 1.0);
        double halfW = widthMm / 2.0;
        double halfH = heightMm / 2.0;

        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP session remoting create rectangle curves"
        );

        NXOpen.Point3d p1 = new NXOpen.Point3d(-halfW, -halfH, 0.0);
        NXOpen.Point3d p2 = new NXOpen.Point3d(halfW, -halfH, 0.0);
        NXOpen.Point3d p3 = new NXOpen.Point3d(halfW, halfH, 0.0);
        NXOpen.Point3d p4 = new NXOpen.Point3d(-halfW, halfH, 0.0);

        NXOpen.Line line1 = workPart.Curves.CreateLine(p1, p2);
        NXOpen.Line line2 = workPart.Curves.CreateLine(p2, p3);
        NXOpen.Line line3 = workPart.Curves.CreateLine(p3, p4);
        NXOpen.Line line4 = workPart.Curves.CreateLine(p4, p1);

        try
        {
            line1.SetName(safeName + "_1");
            line2.SetName(safeName + "_2");
            line3.SetName(safeName + "_3");
            line4.SetName(safeName + "_4");
        }
        catch
        {
        }

        session.UpdateManager.DoUpdate(markId);

        return "{"
            + "\"ok\":true,"
            + "\"created\":true,"
            + "\"object_type\":\"rectangle_curves\","
            + "\"name\":\"" + JsonEscape(safeName) + "\","
            + "\"width_mm\":" + widthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"height_mm\":" + heightMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
            + "}";
    }

    private static string CreateLineCurve(NXOpen.Session session, string name, double x1, double y1, double x2, double y2)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string safeName = SafeName(name, "MCP Line");
        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP create line curve"
        );

        NXOpen.Line line = workPart.Curves.CreateLine(
            new NXOpen.Point3d(x1, y1, 0.0),
            new NXOpen.Point3d(x2, y2, 0.0)
        );

        try
        {
            line.SetName(safeName);
        }
        catch
        {
        }

        session.UpdateManager.DoUpdate(markId);

        return "{"
            + "\"ok\":true,"
            + "\"created\":true,"
            + "\"object_type\":\"line_curve\","
            + "\"name\":\"" + JsonEscape(safeName) + "\","
            + "\"x1_mm\":" + x1.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"y1_mm\":" + y1.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"x2_mm\":" + x2.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"y2_mm\":" + y2.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
            + "}";
    }

    private static string CreateCircleCurve(NXOpen.Session session, string name, double centerX, double centerY, double radiusMm)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string safeName = SafeName(name, "MCP Circle");
        radiusMm = Math.Max(radiusMm, 0.1);

        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP create circle curve"
        );

        NXOpen.Matrix3x3 identity = new NXOpen.Matrix3x3();
        identity.Xx = 1.0;
        identity.Xy = 0.0;
        identity.Xz = 0.0;
        identity.Yx = 0.0;
        identity.Yy = 1.0;
        identity.Yz = 0.0;
        identity.Zx = 0.0;
        identity.Zy = 0.0;
        identity.Zz = 1.0;
        NXOpen.NXMatrix matrix = workPart.NXMatrices.Create(identity);
        NXOpen.Arc arc = workPart.Curves.CreateArc(
            new NXOpen.Point3d(centerX, centerY, 0.0),
            matrix,
            radiusMm,
            0.0,
            Math.PI * 2.0
        );

        try
        {
            arc.SetName(safeName);
        }
        catch
        {
        }

        session.UpdateManager.DoUpdate(markId);

        return "{"
            + "\"ok\":true,"
            + "\"created\":true,"
            + "\"object_type\":\"circle_curve\","
            + "\"name\":\"" + JsonEscape(safeName) + "\","
            + "\"center_x_mm\":" + centerX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"center_y_mm\":" + centerY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"radius_mm\":" + radiusMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
            + "}";
    }

    private static string CreateReferenceCross(NXOpen.Session session, string name, double sizeMm)
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string safeName = SafeName(name, "MCP Reference Cross");
        sizeMm = Math.Max(sizeMm, 1.0);
        double half = sizeMm / 2.0;

        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP create reference cross"
        );

        NXOpen.Line hLine = workPart.Curves.CreateLine(
            new NXOpen.Point3d(-half, 0.0, 0.0),
            new NXOpen.Point3d(half, 0.0, 0.0)
        );
        NXOpen.Line vLine = workPart.Curves.CreateLine(
            new NXOpen.Point3d(0.0, -half, 0.0),
            new NXOpen.Point3d(0.0, half, 0.0)
        );

        try
        {
            hLine.SetName(safeName + "_horizontal");
            vLine.SetName(safeName + "_vertical");
        }
        catch
        {
        }

        session.UpdateManager.DoUpdate(markId);

        return "{"
            + "\"ok\":true,"
            + "\"created\":true,"
            + "\"object_type\":\"reference_cross\","
            + "\"name\":\"" + JsonEscape(safeName) + "\","
            + "\"size_mm\":" + sizeMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
            + "}";
    }

    private static string CreateBoxBody(
        NXOpen.Session session,
        string name,
        double originX,
        double originY,
        double originZ,
        double lengthMm,
        double widthMm,
        double heightMm
    )
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string safeName = SafeName(name, "MCP Box Body");
        lengthMm = Math.Max(lengthMm, 0.1);
        widthMm = Math.Max(widthMm, 0.1);
        heightMm = Math.Max(heightMm, 0.1);

        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP create box body"
        );

        NXOpen.Features.BlockFeatureBuilder blockBuilder = null;
        string featureName = "";
        string bodyName = "";
        int bodyCount = 0;

        try
        {
            NXOpen.Features.Feature nullFeature = null;
            blockBuilder = workPart.Features.CreateBlockFeatureBuilder(nullFeature);
            blockBuilder.Type = NXOpen.Features.BlockFeatureBuilder.Types.OriginAndEdgeLengths;
            blockBuilder.SetOriginAndLengths(
                new NXOpen.Point3d(originX, originY, originZ),
                lengthMm.ToString(System.Globalization.CultureInfo.InvariantCulture),
                widthMm.ToString(System.Globalization.CultureInfo.InvariantCulture),
                heightMm.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );

            NXOpen.Features.Feature feature = blockBuilder.CommitFeature();
            try
            {
                feature.SetName(safeName);
                featureName = feature.Name;
            }
            catch
            {
                featureName = safeName;
            }

            try
            {
                NXOpen.Features.BodyFeature bodyFeature = (NXOpen.Features.BodyFeature)feature;
                NXOpen.Body[] bodies = bodyFeature.GetBodies();
                bodyCount = bodies.Length;
                if (bodies.Length > 0)
                {
                    bodies[0].SetName(safeName + "_body");
                    bodyName = bodies[0].Name;
                }
            }
            catch
            {
            }
        }
        finally
        {
            if (blockBuilder != null)
            {
                blockBuilder.Destroy();
            }
        }

        session.UpdateManager.DoUpdate(markId);

        return "{"
            + "\"ok\":true,"
            + "\"created\":true,"
            + "\"object_type\":\"box_body\","
            + "\"name\":\"" + JsonEscape(safeName) + "\","
            + "\"feature_name\":\"" + JsonEscape(featureName) + "\","
            + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
            + "\"body_count\":" + bodyCount + ","
            + "\"origin_x_mm\":" + originX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"origin_y_mm\":" + originY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"origin_z_mm\":" + originZ.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"length_mm\":" + lengthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"width_mm\":" + widthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"height_mm\":" + heightMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
            + "}";
    }

    private static string CreateExtrudedRectangle(
        NXOpen.Session session,
        string name,
        double centerX,
        double centerY,
        double originZ,
        double widthMm,
        double heightMm,
        double depthMm
    )
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string safeName = SafeName(name, "MCP Extruded Rectangle");
        string objectName = safeName + "_" + DateTime.Now.ToString("HHmmss");
        widthMm = Math.Max(widthMm, 0.1);
        heightMm = Math.Max(heightMm, 0.1);
        depthMm = Math.Max(depthMm, 0.1);
        double halfW = widthMm / 2.0;
        double halfH = heightMm / 2.0;

        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP create extruded rectangle"
        );

        NXOpen.Sketch sketch = null;
        NXOpen.Section section = null;
        NXOpen.Features.ExtrudeBuilder extrudeBuilder = null;
        string featureName = "";
        string bodyName = "";
        int bodyCount = 0;

        try
        {
            NXOpen.Point3d planeOrigin = new NXOpen.Point3d(centerX, centerY, originZ);
            NXOpen.Vector3d normal = new NXOpen.Vector3d(0.0, 0.0, 1.0);
            NXOpen.Plane plane = workPart.Planes.CreatePlane(planeOrigin, normal, NXOpen.SmartObject.UpdateOption.WithinModeling);

            NXOpen.SketchInPlaceBuilder sketchBuilder = workPart.Sketches.CreateSketchInPlaceBuilder2(null);
            sketchBuilder.PlaneReference = plane;
            try
            {
                sketchBuilder.PlaneOption = NXOpen.Sketch.PlaneOption.ExistingPlane;
            }
            catch
            {
            }

            NXOpen.NXObject nxObject = sketchBuilder.Commit();
            sketchBuilder.Destroy();
            sketch = (NXOpen.Sketch)nxObject;
            try
            {
                sketch.SetName(objectName + "_profile");
            }
            catch
            {
            }

            sketch.Activate(NXOpen.Sketch.ViewReorient.False);
            NXOpen.Line line1 = AddSketchLine(session, workPart, sketch, centerX - halfW, centerY - halfH, centerX + halfW, centerY - halfH, objectName + "_p1");
            AddSketchLine(session, workPart, sketch, centerX + halfW, centerY - halfH, centerX + halfW, centerY + halfH, objectName + "_p2");
            AddSketchLine(session, workPart, sketch, centerX + halfW, centerY + halfH, centerX - halfW, centerY + halfH, objectName + "_p3");
            AddSketchLine(session, workPart, sketch, centerX - halfW, centerY + halfH, centerX - halfW, centerY - halfH, objectName + "_p4");
            sketch.Update();
            session.ActiveSketch.Deactivate(NXOpen.Sketch.ViewReorient.False, NXOpen.Sketch.UpdateLevel.Model);

            section = workPart.Sections.CreateSection(0.00095, 0.001, 0.5);
            NXOpen.Features.Feature[] featureArray = new NXOpen.Features.Feature[1];
            featureArray[0] = sketch.Feature;
            NXOpen.CurveFeatureRule curveFeatureRule = workPart.ScRuleFactory.CreateRuleCurveFeature(featureArray);
            NXOpen.SelectionIntentRule[] rules = new NXOpen.SelectionIntentRule[1];
            rules[0] = curveFeatureRule;
            section.AllowSelfIntersection(false);
            section.AddToSection(
                rules,
                line1,
                null,
                null,
                new NXOpen.Point3d(centerX, centerY, originZ),
                NXOpen.Section.Mode.Create
            );

            NXOpen.Features.Feature nullFeature = null;
            extrudeBuilder = workPart.Features.CreateExtrudeBuilder(nullFeature);
            extrudeBuilder.Section = section;
            extrudeBuilder.Direction = workPart.Directions.CreateDirection(
                sketch,
                NXOpen.Sense.Forward,
                NXOpen.SmartObject.UpdateOption.WithinModeling
            );
            NXOpen.GeometricUtilities.LinearLimits linearLimits =
                (NXOpen.GeometricUtilities.LinearLimits)extrudeBuilder.Limits;
            linearLimits.StartExtend.Value.RightHandSide = "0";
            linearLimits.EndExtend.Value.RightHandSide =
                depthMm.ToString(System.Globalization.CultureInfo.InvariantCulture);
            extrudeBuilder.FeatureOptions.BodyType =
                NXOpen.GeometricUtilities.FeatureOptions.BodyStyle.Solid;

            NXOpen.Features.Feature feature = extrudeBuilder.CommitFeature();
            try
            {
                feature.SetName(objectName);
                featureName = feature.Name;
            }
            catch
            {
                featureName = objectName;
            }

            try
            {
                NXOpen.Features.BodyFeature bodyFeature = (NXOpen.Features.BodyFeature)feature;
                NXOpen.Body[] bodies = bodyFeature.GetBodies();
                bodyCount = bodies.Length;
                if (bodies.Length > 0)
                {
                    bodies[0].SetName(objectName + "_body");
                    bodyName = bodies[0].Name;
                }
            }
            catch
            {
            }
        }
        finally
        {
            if (extrudeBuilder != null)
            {
                extrudeBuilder.Destroy();
            }
            if (section != null)
            {
                try { section.Destroy(); } catch { }
            }
        }

        session.UpdateManager.DoUpdate(markId);

        return "{"
            + "\"ok\":true,"
            + "\"created\":true,"
            + "\"object_type\":\"extruded_rectangle\","
            + "\"name\":\"" + JsonEscape(objectName) + "\","
            + "\"feature_name\":\"" + JsonEscape(featureName) + "\","
            + "\"body_name\":\"" + JsonEscape(bodyName) + "\","
            + "\"body_count\":" + bodyCount + ","
            + "\"center_x_mm\":" + centerX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"center_y_mm\":" + centerY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"origin_z_mm\":" + originZ.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"width_mm\":" + widthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"height_mm\":" + heightMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"depth_mm\":" + depthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
            + "}";
    }

    private static string CreateHingeHousingSection(
        NXOpen.Session session,
        string sectionName,
        double widthMm,
        double heightMm,
        double springWallMm,
        double screwWallMm,
        double fpcbFloorMm,
        double sideWallMm,
        string sourceNote
    )
    {
        NXOpen.Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is currently loaded. Create or open a part first.\"}";
        }

        string safeName = string.IsNullOrWhiteSpace(sectionName) ? "MEG Hinge Housing Section" : sectionName;
        if (safeName.Length > 60)
        {
            safeName = safeName.Substring(0, 60);
        }
        string objectName = safeName + "_" + DateTime.Now.ToString("HHmmss");

        widthMm = Math.Max(widthMm, 10.0);
        heightMm = Math.Max(heightMm, 3.0);
        springWallMm = Math.Max(springWallMm, 0.10);
        screwWallMm = Math.Max(screwWallMm, 0.10);
        fpcbFloorMm = Math.Max(fpcbFloorMm, 0.10);
        sideWallMm = Math.Max(sideWallMm, 0.10);

        double halfW = widthMm / 2.0;
        double halfH = heightMm / 2.0;
        double innerLeft = -halfW + sideWallMm;
        double innerRight = halfW - sideWallMm;
        double innerTop = halfH - springWallMm;
        double innerBottom = -halfH + fpcbFloorMm;

        if (innerRight <= innerLeft + 1.0 || innerTop <= innerBottom + 1.0)
        {
            return "{\"ok\":false,\"error\":\"Input wall values are too large for the requested section size.\"}";
        }

        double screwKeepoutHalfWidth = Math.Max(2.0, screwWallMm * 4.0);
        screwKeepoutHalfWidth = Math.Min(screwKeepoutHalfWidth, (innerRight - innerLeft) / 4.0);
        double screwLeft = -screwKeepoutHalfWidth;
        double screwRight = screwKeepoutHalfWidth;

        NXOpen.Session.UndoMarkId markId = session.SetUndoMark(
            NXOpen.Session.MarkVisibility.Visible,
            "MCP create MEG hinge housing section"
        );

        NXOpen.Point3d origin = new NXOpen.Point3d(0.0, 0.0, 0.0);
        NXOpen.Vector3d normal = new NXOpen.Vector3d(0.0, 0.0, 1.0);
        NXOpen.Plane plane = workPart.Planes.CreatePlane(origin, normal, NXOpen.SmartObject.UpdateOption.WithinModeling);

        NXOpen.SketchInPlaceBuilder sketchBuilder = workPart.Sketches.CreateSketchInPlaceBuilder2(null);
        sketchBuilder.PlaneReference = plane;
        try
        {
            sketchBuilder.PlaneOption = NXOpen.Sketch.PlaneOption.ExistingPlane;
        }
        catch
        {
        }

        NXOpen.NXObject nxObject = sketchBuilder.Commit();
        sketchBuilder.Destroy();

        NXOpen.Sketch sketch = (NXOpen.Sketch)nxObject;
        try
        {
            sketch.SetName(objectName);
        }
        catch
        {
        }

        sketch.Activate(NXOpen.Sketch.ViewReorient.False);

        AddSketchLine(session, workPart, sketch, -halfW, -halfH, halfW, -halfH, objectName + "_outer_bottom");
        AddSketchLine(session, workPart, sketch, halfW, -halfH, halfW, halfH, objectName + "_outer_right");
        AddSketchLine(session, workPart, sketch, halfW, halfH, -halfW, halfH, objectName + "_outer_top");
        AddSketchLine(session, workPart, sketch, -halfW, halfH, -halfW, -halfH, objectName + "_outer_left");

        AddSketchLine(session, workPart, sketch, innerLeft, innerBottom, innerRight, innerBottom, objectName + "_fpcb_floor_" + FormatMm(fpcbFloorMm));
        AddSketchLine(session, workPart, sketch, innerRight, innerBottom, innerRight, innerTop, objectName + "_side_wall_right_" + FormatMm(sideWallMm));
        AddSketchLine(session, workPart, sketch, innerRight, innerTop, innerLeft, innerTop, objectName + "_spring_wall_" + FormatMm(springWallMm));
        AddSketchLine(session, workPart, sketch, innerLeft, innerTop, innerLeft, innerBottom, objectName + "_side_wall_left_" + FormatMm(sideWallMm));

        AddSketchLine(session, workPart, sketch, screwLeft, innerBottom, screwLeft, innerTop, objectName + "_screw_keepout_left_" + FormatMm(screwWallMm));
        AddSketchLine(session, workPart, sketch, screwRight, innerBottom, screwRight, innerTop, objectName + "_screw_keepout_right_" + FormatMm(screwWallMm));
        AddSketchLine(session, workPart, sketch, screwLeft, innerTop, screwRight, innerTop, objectName + "_screw_keepout_top");
        AddSketchLine(session, workPart, sketch, screwLeft, innerBottom, screwRight, innerBottom, objectName + "_screw_keepout_bottom");

        AddSketchLine(session, workPart, sketch, 0.0, -halfH, 0.0, halfH, objectName + "_center_reference");

        sketch.Update();
        session.ActiveSketch.Deactivate(NXOpen.Sketch.ViewReorient.False, NXOpen.Sketch.UpdateLevel.Model);
        session.UpdateManager.DoUpdate(markId);

        return "{"
            + "\"ok\":true,"
            + "\"created\":true,"
            + "\"object_type\":\"hinge_housing_basic_section\","
            + "\"requested_name\":\"" + JsonEscape(safeName) + "\","
            + "\"sketch_name\":\"" + JsonEscape(objectName) + "\","
            + "\"outer_width_mm\":" + widthMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"outer_height_mm\":" + heightMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"spring_wall_min_mm\":" + springWallMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"screw_area_wall_min_mm\":" + screwWallMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"fpcb_floor_min_mm\":" + fpcbFloorMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"side_wall_mm\":" + sideWallMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"source_note\":\"" + JsonEscape(sourceNote) + "\","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\""
            + "}";
    }

    private static NXOpen.Line AddSketchLine(
        NXOpen.Session session,
        NXOpen.Part workPart,
        NXOpen.Sketch sketch,
        double x1,
        double y1,
        double x2,
        double y2,
        string name
    )
    {
        NXOpen.Line line = workPart.Curves.CreateLine(
            new NXOpen.Point3d(x1, y1, 0.0),
            new NXOpen.Point3d(x2, y2, 0.0)
        );

        try
        {
            session.ActiveSketch.AddGeometry(line);
        }
        catch
        {
            sketch.AddGeometry(line);
        }

        try
        {
            line.SetName(name);
        }
        catch
        {
        }

        return line;
    }

    private static string FormatMm(double value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "mm";
    }

    private static string Num(double value)
    {
        return value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static NXOpen.Unit[] MassPropertyUnits(NXOpen.Part workPart)
    {
        return new NXOpen.Unit[]
        {
            FindUnit(workPart, "SquareMilliMeter", "Area"),
            FindUnit(workPart, "CubicMilliMeter", "Volume"),
            FindUnit(workPart, "Kilogram", "Mass"),
            FindUnit(workPart, "MilliMeter", "Length"),
            FindUnit(workPart, "Newton", "Force")
        };
    }

    private static NXOpen.Unit FindUnit(NXOpen.Part workPart, string objectName, string measureName)
    {
        try
        {
            return workPart.UnitCollection.FindObject(objectName);
        }
        catch
        {
        }

        try
        {
            return workPart.UnitCollection.GetBase(measureName);
        }
        catch (Exception ex)
        {
            throw new Exception("Could not find NX unit " + objectName + " or base measure " + measureName + ": " + ShortError(ex));
        }
    }

    private static NXOpen.Body FindTargetBody(NXOpen.Part workPart, string targetBodyName)
    {
        NXOpen.Body[] bodies = workPart.Bodies.ToArray();
        string target = string.IsNullOrWhiteSpace(targetBodyName) ? "" : targetBodyName.Trim();
        if (bodies.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            for (int i = bodies.Length - 1; i >= 0; i--)
            {
                bool isSolid = false;
                try { isSolid = bodies[i].IsSolidBody; } catch { }
                if (isSolid)
                {
                    return bodies[i];
                }
            }
            return bodies[bodies.Length - 1];
        }

        for (int i = 0; i < bodies.Length; i++)
        {
            string name = "";
            string id = "";
            try { name = bodies[i].Name; } catch { }
            try { id = bodies[i].JournalIdentifier; } catch { }
            if (String.Equals(name, target, StringComparison.OrdinalIgnoreCase)
                || String.Equals(id, target, StringComparison.OrdinalIgnoreCase))
            {
                return bodies[i];
            }
        }

        return null;
    }

    private static bool TryFaceSampleAtMidUv(NXOpen.UF.UFSession ufSession, NXOpen.Face face, double[] point, double[] normal)
    {
        try
        {
            double[] uvMinMax = new double[4];
            ufSession.Modl.AskFaceUvMinmax(face.Tag, uvMinMax);
            double[] param = new double[]
            {
                (uvMinMax[0] + uvMinMax[1]) / 2.0,
                (uvMinMax[2] + uvMinMax[3]) / 2.0
            };
            double[] u1 = new double[3];
            double[] v1 = new double[3];
            double[] u2 = new double[3];
            double[] v2 = new double[3];
            double[] radii = new double[2];
            ufSession.Modl.AskFaceProps(face.Tag, param, point, u1, v1, u2, v2, normal, radii);
            double length = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
            if (length <= 0.0000001)
            {
                return false;
            }
            normal[0] = normal[0] / length;
            normal[1] = normal[1] / length;
            normal[2] = normal[2] / length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFacePointNormalAtUv(NXOpen.UF.UFSession ufSession, NXOpen.Face face, double u, double v, double[] point, double[] normal)
    {
        try
        {
            double[] param = new double[] { u, v };
            double[] u1 = new double[3];
            double[] v1 = new double[3];
            double[] u2 = new double[3];
            double[] v2 = new double[3];
            double[] radii = new double[2];
            ufSession.Modl.AskFaceProps(face.Tag, param, point, u1, v1, u2, v2, normal, radii);
            double length = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
            if (length <= 0.0000001)
            {
                return false;
            }
            normal[0] = normal[0] / length;
            normal[1] = normal[1] / length;
            normal[2] = normal[2] / length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFaceNormalAtPoint(NXOpen.UF.UFSession ufSession, NXOpen.Face face, double[] nearPoint, double[] normal)
    {
        try
        {
            double[] param = new double[2];
            double[] facePoint = new double[3];
            ufSession.Modl.AskFaceParm(face.Tag, nearPoint, param, facePoint);

            double[] point = new double[3];
            double[] u1 = new double[3];
            double[] v1 = new double[3];
            double[] u2 = new double[3];
            double[] v2 = new double[3];
            double[] radii = new double[2];
            ufSession.Modl.AskFaceProps(face.Tag, param, point, u1, v1, u2, v2, normal, radii);
            double length = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
            if (length <= 0.0000001)
            {
                return false;
            }
            normal[0] = normal[0] / length;
            normal[1] = normal[1] / length;
            normal[2] = normal[2] / length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int FindFaceIndexByTag(NXOpen.Face[] faces, NXOpen.Tag tag)
    {
        for (int i = 0; i < faces.Length; i++)
        {
            try
            {
                if (faces[i].Tag == tag)
                {
                    return i;
                }
            }
            catch
            {
            }
        }
        return -1;
    }

    private static bool IsHoleLikeFaceType(string faceType)
    {
        if (string.IsNullOrWhiteSpace(faceType))
        {
            return false;
        }

        return faceType.IndexOf("Revolution", StringComparison.OrdinalIgnoreCase) >= 0
            || faceType.IndexOf("Cylind", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsNonWallDetailFaceType(string faceType)
    {
        if (string.IsNullOrWhiteSpace(faceType))
        {
            return false;
        }

        return faceType.IndexOf("Blend", StringComparison.OrdinalIgnoreCase) >= 0
            || faceType.IndexOf("Conical", StringComparison.OrdinalIgnoreCase) >= 0
            || faceType.IndexOf("Torus", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsStableWallFaceType(string faceType)
    {
        if (string.IsNullOrWhiteSpace(faceType))
        {
            return false;
        }

        return faceType.IndexOf("Planar", StringComparison.OrdinalIgnoreCase) >= 0
            || faceType.IndexOf("Cylind", StringComparison.OrdinalIgnoreCase) >= 0
            || faceType.IndexOf("Revolution", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryCylinderPlaneWallThickness(
        NXOpen.UF.UFSession ufSession,
        NXOpen.Face faceA,
        string faceTypeA,
        NXOpen.Face faceB,
        string faceTypeB,
        out double thickness,
        out double axisPlaneNormalDot)
    {
        thickness = 0.0;
        axisPlaneNormalDot = 0.0;

        NXOpen.Face cylinderFace = null;
        NXOpen.Face planeFace = null;
        if (IsCylindricalWallFaceType(faceTypeA) && IsPlanarWallFaceType(faceTypeB))
        {
            cylinderFace = faceA;
            planeFace = faceB;
        }
        else if (IsCylindricalWallFaceType(faceTypeB) && IsPlanarWallFaceType(faceTypeA))
        {
            cylinderFace = faceB;
            planeFace = faceA;
        }
        else
        {
            return false;
        }

        try
        {
            int cylinderType = 0;
            double[] cylinderPoint = new double[3];
            double[] cylinderDir = new double[3];
            double[] cylinderBox = new double[6];
            double cylinderRadius = 0.0;
            double cylinderRadData = 0.0;
            int cylinderNormDir = 0;
            ufSession.Modl.AskFaceData(
                cylinderFace.Tag,
                out cylinderType,
                cylinderPoint,
                cylinderDir,
                cylinderBox,
                out cylinderRadius,
                out cylinderRadData,
                out cylinderNormDir
            );

            int planeType = 0;
            double[] planePoint = new double[3];
            double[] planeNormal = new double[3];
            double[] planeBox = new double[6];
            double planeRadius = 0.0;
            double planeRadData = 0.0;
            int planeNormDir = 0;
            ufSession.Modl.AskFaceData(
                planeFace.Tag,
                out planeType,
                planePoint,
                planeNormal,
                planeBox,
                out planeRadius,
                out planeRadData,
                out planeNormDir
            );

            if (!Normalize(cylinderDir) || !Normalize(planeNormal))
            {
                return false;
            }

            axisPlaneNormalDot = Math.Abs(Dot(cylinderDir, planeNormal));
            if (axisPlaneNormalDot > 0.15)
            {
                return false;
            }

            double[] axisToPlane = new double[]
            {
                cylinderPoint[0] - planePoint[0],
                cylinderPoint[1] - planePoint[1],
                cylinderPoint[2] - planePoint[2]
            };
            double axisPlaneDistance = Math.Abs(Dot(axisToPlane, planeNormal));
            thickness = axisPlaneDistance - Math.Abs(cylinderRadius);
            if (Double.IsNaN(thickness) || Double.IsInfinity(thickness) || thickness <= 0.0)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPlanarWallFaceType(string faceType)
    {
        return !string.IsNullOrWhiteSpace(faceType)
            && faceType.IndexOf("Planar", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsCylindricalWallFaceType(string faceType)
    {
        return !string.IsNullOrWhiteSpace(faceType)
            && (faceType.IndexOf("Cylind", StringComparison.OrdinalIgnoreCase) >= 0
                || faceType.IndexOf("Revolution", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool TryFaceEdgeBoundingBox(NXOpen.Face face, double[] minPoint, double[] maxPoint)
    {
        bool hasPoint = false;
        try
        {
            NXOpen.Edge[] edges = face.GetEdges();
            for (int i = 0; i < edges.Length; i++)
            {
                NXOpen.Point3d first = new NXOpen.Point3d();
                NXOpen.Point3d second = new NXOpen.Point3d();
                try
                {
                    edges[i].GetVertices(out first, out second);
                    ExtendArrayBounds(first.X, first.Y, first.Z, ref hasPoint, minPoint, maxPoint);
                    ExtendArrayBounds(second.X, second.Y, second.Z, ref hasPoint, minPoint, maxPoint);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return hasPoint;
    }

    private static void ExtendArrayBounds(
        double x,
        double y,
        double z,
        ref bool hasBounds,
        double[] minPoint,
        double[] maxPoint)
    {
        if (!hasBounds)
        {
            minPoint[0] = x;
            minPoint[1] = y;
            minPoint[2] = z;
            maxPoint[0] = x;
            maxPoint[1] = y;
            maxPoint[2] = z;
            hasBounds = true;
            return;
        }

        if (x < minPoint[0]) { minPoint[0] = x; }
        if (y < minPoint[1]) { minPoint[1] = y; }
        if (z < minPoint[2]) { minPoint[2] = z; }
        if (x > maxPoint[0]) { maxPoint[0] = x; }
        if (y > maxPoint[1]) { maxPoint[1] = y; }
        if (z > maxPoint[2]) { maxPoint[2] = z; }
    }

    private static double AabbDistance(double[] minA, double[] maxA, double[] minB, double[] maxB)
    {
        double dx = 0.0;
        double dy = 0.0;
        double dz = 0.0;

        if (maxA[0] < minB[0]) { dx = minB[0] - maxA[0]; }
        else if (maxB[0] < minA[0]) { dx = minA[0] - maxB[0]; }

        if (maxA[1] < minB[1]) { dy = minB[1] - maxA[1]; }
        else if (maxB[1] < minA[1]) { dy = minA[1] - maxB[1]; }

        if (maxA[2] < minB[2]) { dz = minB[2] - maxA[2]; }
        else if (maxB[2] < minA[2]) { dz = minA[2] - maxB[2]; }

        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double BoxCenterDistance(double[] minA, double[] maxA, double[] minB, double[] maxB)
    {
        double ax = (minA[0] + maxA[0]) / 2.0;
        double ay = (minA[1] + maxA[1]) / 2.0;
        double az = (minA[2] + maxA[2]) / 2.0;
        double bx = (minB[0] + maxB[0]) / 2.0;
        double by = (minB[1] + maxB[1]) / 2.0;
        double bz = (minB[2] + maxB[2]) / 2.0;
        double dx = bx - ax;
        double dy = by - ay;
        double dz = bz - az;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static bool IsCurvedWallSourceFace(NXOpen.UF.UFSession ufSession, NXOpen.Face face, string faceType)
    {
        if (IsNonWallDetailFaceType(faceType))
        {
            return false;
        }
        if (faceType.IndexOf("Planar", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        try
        {
            double[] uvMinMax = new double[4];
            ufSession.Modl.AskFaceUvMinmax(face.Tag, uvMinMax);
            double[] param = new double[]
            {
                (uvMinMax[0] + uvMinMax[1]) / 2.0,
                (uvMinMax[2] + uvMinMax[3]) / 2.0
            };
            double[] point = new double[3];
            double[] u1 = new double[3];
            double[] v1 = new double[3];
            double[] u2 = new double[3];
            double[] v2 = new double[3];
            double[] normal = new double[3];
            double[] radii = new double[2];
            ufSession.Modl.AskFaceProps(face.Tag, param, point, u1, v1, u2, v2, normal, radii);
            for (int i = 0; i < radii.Length; i++)
            {
                double radius = Math.Abs(radii[i]);
                if (radius > 0.05 && radius < 300.0)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static double TryFaceUvInteriorMargin(NXOpen.UF.UFSession ufSession, NXOpen.Face face, double[] nearPoint)
    {
        try
        {
            double[] param = new double[2];
            double[] facePoint = new double[3];
            ufSession.Modl.AskFaceParm(face.Tag, nearPoint, param, facePoint);
            double[] uvMinMax = new double[4];
            ufSession.Modl.AskFaceUvMinmax(face.Tag, uvMinMax);
            double uRange = uvMinMax[1] - uvMinMax[0];
            double vRange = uvMinMax[3] - uvMinMax[2];
            if (Math.Abs(uRange) <= 0.0000001 || Math.Abs(vRange) <= 0.0000001)
            {
                return -1.0;
            }

            double uMargin = Math.Min((param[0] - uvMinMax[0]) / uRange, (uvMinMax[1] - param[0]) / uRange);
            double vMargin = Math.Min((param[1] - uvMinMax[2]) / vRange, (uvMinMax[3] - param[1]) / vRange);
            double margin = Math.Min(uMargin, vMargin);
            if (Double.IsNaN(margin) || Double.IsInfinity(margin))
            {
                return -1.0;
            }
            return margin;
        }
        catch
        {
            return -1.0;
        }
    }

    private static double[] IdentityTransform()
    {
        return new double[]
        {
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            0.0, 0.0, 0.0, 1.0
        };
    }

    private static double Dot(double[] a, double[] b)
    {
        return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    }

    private static void Cross(double[] a, double[] b, double[] result)
    {
        result[0] = (a[1] * b[2]) - (a[2] * b[1]);
        result[1] = (a[2] * b[0]) - (a[0] * b[2]);
        result[2] = (a[0] * b[1]) - (a[1] * b[0]);
    }

    private static bool Normalize(double[] vector)
    {
        double length = Math.Sqrt(vector[0] * vector[0] + vector[1] * vector[1] + vector[2] * vector[2]);
        if (length <= 0.0000001 || Double.IsNaN(length) || Double.IsInfinity(length))
        {
            return false;
        }
        vector[0] = vector[0] / length;
        vector[1] = vector[1] / length;
        vector[2] = vector[2] / length;
        return true;
    }

    private static bool Normalize2D(ref double u, ref double v)
    {
        double length = Math.Sqrt(u * u + v * v);
        if (length <= 0.0000001 || Double.IsNaN(length) || Double.IsInfinity(length))
        {
            return false;
        }
        u = u / length;
        v = v / length;
        return true;
    }

    private static double Distance(double[] a, double[] b)
    {
        double dx = b[0] - a[0];
        double dy = b[1] - a[1];
        double dz = b[2] - a[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static void InsertCandidate(
        int[] candidateA,
        int[] candidateB,
        double[] candidateApprox,
        double[] candidateDot,
        ref int candidateCount,
        int maxCandidates,
        int faceA,
        int faceB,
        double approximateThickness,
        double normalDot)
    {
        int insertAt = candidateCount;
        for (int i = 0; i < candidateCount; i++)
        {
            if (approximateThickness < candidateApprox[i])
            {
                insertAt = i;
                break;
            }
        }

        if (candidateCount >= maxCandidates && insertAt >= maxCandidates)
        {
            return;
        }

        int last = candidateCount < maxCandidates ? candidateCount : maxCandidates - 1;
        for (int i = last; i > insertAt; i--)
        {
            candidateA[i] = candidateA[i - 1];
            candidateB[i] = candidateB[i - 1];
            candidateApprox[i] = candidateApprox[i - 1];
            candidateDot[i] = candidateDot[i - 1];
        }

        candidateA[insertAt] = faceA;
        candidateB[insertAt] = faceB;
        candidateApprox[insertAt] = approximateThickness;
        candidateDot[insertAt] = normalDot;
        if (candidateCount < maxCandidates)
        {
            candidateCount++;
        }
    }

    private static void InsertWallReportCandidate(
        WallThicknessCandidateReport[] candidates,
        ref int candidateCount,
        int maxCandidates,
        int faceA,
        int faceB,
        double distance,
        double approx,
        double alignmentA,
        double alignmentB,
        double normalDot,
        double uvMarginA,
        double uvMarginB,
        double[] pointA,
        double[] pointB,
        double sortKey)
    {
        if (maxCandidates <= 0)
        {
            return;
        }

        int insertAt = candidateCount;
        for (int i = 0; i < candidateCount; i++)
        {
            double existingKey = candidates[i] == null ? Double.MaxValue : candidates[i].SortKey;
            if (sortKey < existingKey)
            {
                insertAt = i;
                break;
            }
        }

        if (candidateCount >= maxCandidates && insertAt >= maxCandidates)
        {
            return;
        }

        int last = candidateCount < maxCandidates ? candidateCount : maxCandidates - 1;
        for (int i = last; i > insertAt; i--)
        {
            candidates[i] = candidates[i - 1];
        }

        WallThicknessCandidateReport report = new WallThicknessCandidateReport();
        report.FaceA = faceA;
        report.FaceB = faceB;
        report.Distance = distance;
        report.Approx = approx;
        report.AlignmentA = alignmentA;
        report.AlignmentB = alignmentB;
        report.NormalDot = normalDot;
        report.UvMarginA = uvMarginA;
        report.UvMarginB = uvMarginB;
        report.SortKey = sortKey;
        CopyPoint(pointA, report.PointA);
        CopyPoint(pointB, report.PointB);
        candidates[insertAt] = report;
        if (candidateCount < maxCandidates)
        {
            candidateCount++;
        }
    }

    private static string WallCandidateArrayJson(
        WallThicknessCandidateReport[] candidates,
        int candidateCount,
        NXOpen.Face[] faces,
        string[] faceTypes)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("[");
        for (int i = 0; i < candidateCount; i++)
        {
            if (i > 0)
            {
                sb.Append(",");
            }
            WallThicknessCandidateReport candidate = candidates[i];
            string faceAName = "";
            string faceBName = "";
            string faceAJournalId = "";
            string faceBJournalId = "";
            try { faceAName = faces[candidate.FaceA].Name; } catch { }
            try { faceBName = faces[candidate.FaceB].Name; } catch { }
            try { faceAJournalId = faces[candidate.FaceA].JournalIdentifier; } catch { }
            try { faceBJournalId = faces[candidate.FaceB].JournalIdentifier; } catch { }

            sb.Append("{");
            sb.Append("\"rank\":").Append(i + 1).Append(",");
            sb.Append("\"thickness_mm\":").Append(Num(candidate.Distance)).Append(",");
            sb.Append("\"aabb_distance_mm\":").Append(Num(candidate.Approx)).Append(",");
            sb.Append("\"normal_alignment\":{\"face_a\":").Append(Num(candidate.AlignmentA)).Append(",\"face_b\":").Append(Num(candidate.AlignmentB)).Append("},");
            sb.Append("\"normal_dot\":").Append(Num(candidate.NormalDot)).Append(",");
            sb.Append("\"uv_margin\":{\"face_a\":").Append(Num(candidate.UvMarginA)).Append(",\"face_b\":").Append(Num(candidate.UvMarginB)).Append("},");
            sb.Append("\"face_a\":{");
            sb.Append("\"index\":").Append(candidate.FaceA).Append(",");
            sb.Append("\"name\":\"").Append(JsonEscape(faceAName)).Append("\",");
            sb.Append("\"journal_id\":\"").Append(JsonEscape(faceAJournalId)).Append("\",");
            sb.Append("\"face_type\":\"").Append(JsonEscape(faceTypes[candidate.FaceA])).Append("\",");
            sb.Append("\"nearest_point_mm\":[").Append(Num(candidate.PointA[0])).Append(",").Append(Num(candidate.PointA[1])).Append(",").Append(Num(candidate.PointA[2])).Append("]");
            sb.Append("},");
            sb.Append("\"face_b\":{");
            sb.Append("\"index\":").Append(candidate.FaceB).Append(",");
            sb.Append("\"name\":\"").Append(JsonEscape(faceBName)).Append("\",");
            sb.Append("\"journal_id\":\"").Append(JsonEscape(faceBJournalId)).Append("\",");
            sb.Append("\"face_type\":\"").Append(JsonEscape(faceTypes[candidate.FaceB])).Append("\",");
            sb.Append("\"nearest_point_mm\":[").Append(Num(candidate.PointB[0])).Append(",").Append(Num(candidate.PointB[1])).Append(",").Append(Num(candidate.PointB[2])).Append("]");
            sb.Append("}");
            sb.Append("}");
        }
        sb.Append("]");
        return sb.ToString();
    }

    private static void CopyPoint(double[] source, double[] target)
    {
        target[0] = source[0];
        target[1] = source[1];
        target[2] = source[2];
    }

    private static void ExtendBounds(
        NXOpen.Point3d point,
        ref bool hasBounds,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        if (!hasBounds)
        {
            minX = point.X;
            minY = point.Y;
            minZ = point.Z;
            maxX = point.X;
            maxY = point.Y;
            maxZ = point.Z;
            hasBounds = true;
            return;
        }

        if (point.X < minX) { minX = point.X; }
        if (point.Y < minY) { minY = point.Y; }
        if (point.Z < minZ) { minZ = point.Z; }
        if (point.X > maxX) { maxX = point.X; }
        if (point.Y > maxY) { maxY = point.Y; }
        if (point.Z > maxZ) { maxZ = point.Z; }
    }

    private static string ShortError(Exception ex)
    {
        if (ex == null)
        {
            return "";
        }

        string message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = ex.GetType().Name;
        }
        message = message.Replace("\r", " ").Replace("\n", " ").Trim();
        if (message.Length > 180)
        {
            message = message.Substring(0, 180);
        }
        return message;
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized == "1" || normalized == "true" || normalized == "yes" || normalized == "y")
        {
            return true;
        }
        if (normalized == "0" || normalized == "false" || normalized == "no" || normalized == "n")
        {
            return false;
        }
        return defaultValue;
    }

    private static string SafeName(string value, string fallback)
    {
        string safeName = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (safeName.Length > 60)
        {
            safeName = safeName.Substring(0, 60);
        }
        return safeName;
    }

    public static Assembly ResolveNxAssembly(object sender, ResolveEventArgs args)
    {
        string simpleName = new AssemblyName(args.Name).Name + ".dll";
        string nxBaseDir = Environment.GetEnvironmentVariable("UGII_BASE_DIR");
        string[] roots = new string[]
        {
            string.IsNullOrWhiteSpace(nxBaseDir) ? "" : Path.Combine(nxBaseDir, "nxbin", "managed"),
            string.IsNullOrWhiteSpace(nxBaseDir) ? "" : Path.Combine(nxBaseDir, "nxbin", "managed_core"),
            @"C:\SCAD\NX2406\NXBIN\managed",
            @"C:\SCAD\NX2406\NXBIN\managed_core"
        };

        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }
            string candidate = Path.Combine(root, simpleName);
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }

        return null;
    }

    private static string BoolJson(bool value)
    {
        return value ? "true" : "false";
    }

    private static string JsonEscape(string value)
    {
        if (value == null)
        {
            return "";
        }
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string XmlEscape(string value)
    {
        if (value == null)
        {
            return "";
        }
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string SafeExceptionMessage(Exception ex)
    {
        if (ex == null)
        {
            return "Unknown exception";
        }

        string typeName = "Exception";
        try
        {
            typeName = ex.GetType().FullName;
        }
        catch
        {
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(ex.Message))
            {
                return typeName + ": " + ex.Message;
            }
        }
        catch
        {
        }

        return typeName + ": message unavailable. Is NxMcpSessionServer.dll loaded in NX?";
    }
}
