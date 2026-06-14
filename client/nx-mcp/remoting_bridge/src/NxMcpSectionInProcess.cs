using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NXOpen;
using NXOpen.UF;

public class NxMcpSectionInProcess
{
    private class SamplePoint
    {
        public int SegmentIndex;
        public int PointIndex;
        public double X;
        public double Y;
        public double Z;
        public double U;
        public double V;
        public double Tu;
        public double Tv;
        public int BodyIndex;
        public string BodyName = "";
        public string BodyJournalId = "";
    }

    private class ThicknessResult
    {
        public bool Found;
        public double Distance = Double.MaxValue;
        public SamplePoint A;
        public SamplePoint B;
        public string Method = "";
    }

    public static void ExecuteMe(
        string targetBodyName,
        double planeX,
        double planeY,
        double planeZ,
        double normalX,
        double normalY,
        double normalZ,
        int samplesPerSegment,
        double minCandidateThickness,
        string outputDir,
        string responsePath)
    {
        try
        {
            string json = Run(
                targetBodyName,
                planeX,
                planeY,
                planeZ,
                normalX,
                normalY,
                normalZ,
                samplesPerSegment,
                minCandidateThickness,
                outputDir
            );
            File.WriteAllText(responsePath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                responsePath,
                "{\"ok\":false,\"error\":\"" + JsonEscape(SafeMessage(ex)) + "\"}",
                Encoding.UTF8
            );
        }
    }

    private static string Run(
        string targetBodyName,
        double planeX,
        double planeY,
        double planeZ,
        double normalX,
        double normalY,
        double normalZ,
        int samplesPerSegment,
        double minCandidateThickness,
        string outputDir)
    {
        Session session = Session.GetSession();
        UFSession uf = UFSession.GetUFSession();
        Part workPart = session.Parts.Work;
        if (workPart == null)
        {
            return "{\"ok\":false,\"error\":\"No work part is loaded.\"}";
        }

        Body[] targetBodies = FindTargetBodies(workPart, targetBodyName);
        if (targetBodies.Length == 0)
        {
            return "{\"ok\":false,\"error\":\"No matching solid bodies found.\",\"target_body_name\":\"" + JsonEscape(targetBodyName) + "\"}";
        }

        double[] origin = new double[] { planeX, planeY, planeZ };
        double[] normal = new double[] { normalX, normalY, normalZ };
        if (!Normalize(normal))
        {
            return "{\"ok\":false,\"error\":\"Invalid section plane normal.\"}";
        }
        int requestedBodyCount = targetBodies.Length;
        targetBodies = FilterBodiesIntersectingPlane(uf, targetBodies, origin, normal, 0.75);
        if (targetBodies.Length == 0)
        {
            return "{\"ok\":false,\"error\":\"No requested solid bodies intersect the section plane bounding box.\",\"target_body_name\":\""
                + JsonEscape(targetBodyName)
                + "\",\"requested_body_count\":" + requestedBodyCount + "}";
        }
        double[] basisU = new double[3];
        double[] basisV = new double[3];
        SectionBasis(normal, basisU, basisV);

        samplesPerSegment = Math.Max(2, Math.Min(samplesPerSegment, 32));
        minCandidateThickness = Math.Max(0.0, minCandidateThickness);
        Directory.CreateDirectory(outputDir);

        int facetCount = 0;
        int segmentCount = 0;
        List<List<SamplePoint>> polylines = new List<List<SamplePoint>>();
        List<SamplePoint> points = new List<SamplePoint>();

        for (int bodyIndex = 0; bodyIndex < targetBodies.Length; bodyIndex++)
        {
            Body body = targetBodies[bodyIndex];
            string bodyName = "";
            string bodyJournalId = "";
            try { bodyName = body.Name; } catch { }
            try { bodyJournalId = body.JournalIdentifier; } catch { }

            NXOpen.Tag facetModel = NXOpen.Tag.Null;
            try
            {
                UFFacet.Parameters parameters = new UFFacet.Parameters();
                uf.Facet.AskDefaultParameters(out parameters);
                parameters.max_facet_edges = 3;
                parameters.specify_surface_tolerance = true;
                parameters.surface_dist_tolerance = targetBodies.Length > 1 ? 0.12 : 0.06;
                parameters.surface_angular_tolerance = 0.15;
                parameters.specify_curve_tolerance = true;
                parameters.curve_dist_tolerance = targetBodies.Length > 1 ? 0.12 : 0.06;
                parameters.curve_angular_tolerance = 0.15;
                parameters.specify_max_facet_size = true;
                parameters.max_facet_size = targetBodies.Length > 1 ? 2.0 : 1.2;
                parameters.store_face_tags = true;
                uf.Facet.FacetSolid(body.Tag, ref parameters, out facetModel);

                int facetId = 0;
                while (true)
                {
                    uf.Facet.CycleFacets(facetModel, ref facetId);
                    if (facetId == 0)
                    {
                        break;
                    }
                    facetCount++;
                    if (facetCount > 1500000)
                    {
                        break;
                    }

                    int vertexCount = 0;
                    double[,] vertices = new double[3, 3];
                    try
                    {
                        uf.Facet.AskVerticesOfFacet(facetModel, facetId, out vertexCount, vertices);
                    }
                    catch
                    {
                        continue;
                    }
                    if (vertexCount < 3)
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
                        List<SamplePoint> segment = SampleSegment(intersections[i], intersections[i + 1], segmentCount, origin, basisU, basisV, samplesPerSegment, bodyIndex, bodyName, bodyJournalId);
                        if (segment.Count < 2)
                        {
                            continue;
                        }
                        polylines.Add(segment);
                        for (int j = 0; j < segment.Count; j++)
                        {
                            points.Add(segment[j]);
                        }
                        segmentCount++;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (facetModel != NXOpen.Tag.Null)
                {
                    try { uf.Facet.DeleteAllFacetsFromModel(facetModel); } catch { }
                }
            }
        }

        if (points.Count < 2)
        {
            return "{\"ok\":false,\"error\":\"Section plane did not intersect body facets.\",\"body_count\":" + targetBodies.Length + ",\"facet_count\":" + facetCount + "}";
        }

        if (points.Count > 4500)
        {
            int stride = (int)Math.Ceiling((double)points.Count / 4500.0);
            List<SamplePoint> reduced = new List<SamplePoint>();
            for (int i = 0; i < points.Count; i += stride)
            {
                reduced.Add(points[i]);
            }
            points = reduced;
        }

        ThicknessResult raw = FindThickness(uf, targetBodies, points, minCandidateThickness, false);
        ThicknessResult wall = FindThickness(uf, targetBodies, points, minCandidateThickness, true);
        ThicknessResult selected = wall.Found ? wall : raw;

        double minU = Double.MaxValue;
        double minV = Double.MaxValue;
        double maxU = -Double.MaxValue;
        double maxV = -Double.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].U < minU) { minU = points[i].U; }
            if (points[i].V < minV) { minV = points[i].V; }
            if (points[i].U > maxU) { maxU = points[i].U; }
            if (points[i].V > maxV) { maxV = points[i].V; }
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string svgPath = Path.Combine(outputDir, "nx_inprocess_section_y_" + planeY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "p") + "_" + timestamp + ".svg");
        WriteSvg(svgPath, polylines, selected, minU, minV, maxU, maxV, planeY);

        return "{"
            + "\"ok\":true,"
            + "\"analysis_type\":\"inprocess_faceted_section_slice_wall_thickness\","
            + "\"method\":\"nx_inprocess_facet_plane_intersection\","
            + "\"note\":\"Runs inside NX via Session.Execute, facets all requested solid bodies, intersects facets with the requested plane, exports SVG, and estimates 2D wall thickness using inside-union midpoint checks.\","
            + "\"work_part_name\":\"" + JsonEscape(workPart.Name) + "\","
            + "\"target_body_name\":\"" + JsonEscape(targetBodyName) + "\","
            + "\"requested_body_count\":" + requestedBodyCount + ","
            + "\"body_count\":" + targetBodies.Length + ","
            + "\"analyzed_bodies\":" + BodiesJson(targetBodies) + ","
            + "\"plane_point_mm\":[" + Num(planeX) + "," + Num(planeY) + "," + Num(planeZ) + "],"
            + "\"plane_normal\":[" + Num(normal[0]) + "," + Num(normal[1]) + "," + Num(normal[2]) + "],"
            + "\"facet_count\":" + facetCount + ","
            + "\"section_segment_count\":" + segmentCount + ","
            + "\"sample_point_count\":" + points.Count + ","
            + "\"samples_per_segment\":" + samplesPerSegment + ","
            + "\"facet_surface_dist_tolerance_mm\":" + Num(targetBodies.Length > 1 ? 0.12 : 0.06) + ","
            + "\"raw_min_thickness\":" + ThicknessJson(raw) + ","
            + "\"wall_min_thickness\":" + ThicknessJson(wall) + ","
            + "\"selected_min_thickness\":" + ThicknessJson(selected) + ","
            + "\"image_path\":\"" + JsonEscape(svgPath) + "\""
            + "}";
    }

    private static Body[] FindTargetBodies(Part workPart, string targetBodyName)
    {
        Body[] bodies = workPart.Bodies.ToArray();
        string target = string.IsNullOrWhiteSpace(targetBodyName) ? "" : targetBodyName.Trim();
        List<Body> matches = new List<Body>();
        if (bodies.Length == 0)
        {
            return matches.ToArray();
        }
        bool wantsAll = string.IsNullOrWhiteSpace(target)
            || String.Equals(target, "ALL", StringComparison.OrdinalIgnoreCase)
            || String.Equals(target, "*", StringComparison.OrdinalIgnoreCase);
        if (wantsAll)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                try
                {
                    if (bodies[i].IsSolidBody)
                    {
                        matches.Add(bodies[i]);
                    }
                }
                catch { }
            }
            return matches.ToArray();
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
                try
                {
                    if (bodies[i].IsSolidBody)
                    {
                        matches.Add(bodies[i]);
                    }
                }
                catch { }
            }
        }
        return matches.ToArray();
    }

    private static Body[] FilterBodiesIntersectingPlane(UFSession uf, Body[] bodies, double[] origin, double[] normal, double tolerance)
    {
        List<Body> matches = new List<Body>();
        for (int i = 0; i < bodies.Length; i++)
        {
            if (BodyBoxIntersectsPlane(uf, bodies[i], origin, normal, tolerance))
            {
                matches.Add(bodies[i]);
            }
        }
        return matches.ToArray();
    }

    private static bool BodyBoxIntersectsPlane(UFSession uf, Body body, double[] origin, double[] normal, double tolerance)
    {
        double[] box = new double[6];
        try
        {
            uf.Modl.AskBoundingBox(body.Tag, box);
        }
        catch
        {
            return true;
        }

        double minX = Math.Min(box[0], box[3]);
        double minY = Math.Min(box[1], box[4]);
        double minZ = Math.Min(box[2], box[5]);
        double maxX = Math.Max(box[0], box[3]);
        double maxY = Math.Max(box[1], box[4]);
        double maxZ = Math.Max(box[2], box[5]);

        double minDistance = Double.MaxValue;
        double maxDistance = -Double.MaxValue;
        for (int ix = 0; ix < 2; ix++)
        {
            for (int iy = 0; iy < 2; iy++)
            {
                for (int iz = 0; iz < 2; iz++)
                {
                    double[] corner = new double[]
                    {
                        ix == 0 ? minX : maxX,
                        iy == 0 ? minY : maxY,
                        iz == 0 ? minZ : maxZ
                    };
                    double distance = SignedDistance(corner, origin, normal);
                    if (distance < minDistance) { minDistance = distance; }
                    if (distance > maxDistance) { maxDistance = distance; }
                }
            }
        }
        return minDistance <= tolerance && maxDistance >= -tolerance;
    }

    private static ThicknessResult FindThickness(UFSession uf, Body[] bodies, List<SamplePoint> points, double minCandidateThickness, bool tangentFilter)
    {
        ThicknessResult result = new ThicknessResult();
        result.Method = tangentFilter ? "2d_boundary_distance_tangent_filter_inside_union" : "2d_boundary_distance_inside_union";
        double best = 50.0;
        double best2 = best * best;
        double tangentDotMax = 0.35;
        for (int i = 0; i < points.Count; i++)
        {
            SamplePoint a = points[i];
            for (int j = i + 1; j < points.Count; j++)
            {
                SamplePoint b = points[j];
                if (a.SegmentIndex == b.SegmentIndex)
                {
                    continue;
                }
                double du = b.U - a.U;
                double dv = b.V - a.V;
                double d2 = du * du + dv * dv;
                if (d2 <= minCandidateThickness * minCandidateThickness || d2 >= best2)
                {
                    continue;
                }
                double d = Math.Sqrt(d2);
                double dirU = du / d;
                double dirV = dv / d;
                if (tangentFilter)
                {
                    if (Math.Abs(dirU * a.Tu + dirV * a.Tv) > tangentDotMax
                        || Math.Abs(dirU * b.Tu + dirV * b.Tv) > tangentDotMax)
                    {
                        continue;
                    }
                }
                double[] midpoint = new double[] { (a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0 };
                if (!IsInsideAnyBody(uf, bodies, midpoint))
                {
                    continue;
                }
                result.Found = true;
                result.Distance = d;
                result.A = a;
                result.B = b;
                best2 = d2;
            }
        }
        return result;
    }

    private static bool IsInsideAnyBody(UFSession uf, Body[] bodies, double[] point)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            int containment = 0;
            try
            {
                uf.Modl.AskPointContainment(point, bodies[i].Tag, out containment);
                if (containment == 1)
                {
                    return true;
                }
            }
            catch
            {
            }
        }
        return false;
    }

    private static List<SamplePoint> SampleSegment(
        double[] a,
        double[] b,
        int segmentIndex,
        double[] origin,
        double[] basisU,
        double[] basisV,
        int samplesPerSegment,
        int bodyIndex,
        string bodyName,
        string bodyJournalId)
    {
        List<SamplePoint> points = new List<SamplePoint>();
        double dx = b[0] - a[0];
        double dy = b[1] - a[1];
        double dz = b[2] - a[2];
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int count = Math.Max(2, Math.Min(samplesPerSegment, (int)Math.Ceiling(length / 0.08) + 1));
        double tangentU = Dot(new double[] { dx, dy, dz }, basisU);
        double tangentV = Dot(new double[] { dx, dy, dz }, basisV);
        Normalize2D(ref tangentU, ref tangentV);
        for (int i = 0; i < count; i++)
        {
            double t = count <= 1 ? 0.0 : (double)i / (double)(count - 1);
            SamplePoint p = new SamplePoint();
            p.SegmentIndex = segmentIndex;
            p.PointIndex = i;
            p.X = a[0] + dx * t;
            p.Y = a[1] + dy * t;
            p.Z = a[2] + dz * t;
            double[] relative = new double[] { p.X - origin[0], p.Y - origin[1], p.Z - origin[2] };
            p.U = Dot(relative, basisU);
            p.V = Dot(relative, basisV);
            p.Tu = tangentU;
            p.Tv = tangentV;
            p.BodyIndex = bodyIndex;
            p.BodyName = bodyName;
            p.BodyJournalId = bodyJournalId;
            points.Add(p);
        }
        return points;
    }

    private static List<double[]> IntersectPolygonWithPlane(double[][] polygon, double[] origin, double[] normal, double tolerance)
    {
        List<double[]> intersections = new List<double[]>();
        for (int i = 0; i < polygon.Length; i++)
        {
            double[] a = polygon[i];
            double[] b = polygon[(i + 1) % polygon.Length];
            double da = SignedDistance(a, origin, normal);
            double db = SignedDistance(b, origin, normal);
            if (Math.Abs(da) <= tolerance)
            {
                AddUnique(intersections, a, tolerance * 10.0);
            }
            if ((da > tolerance && db < -tolerance) || (da < -tolerance && db > tolerance))
            {
                double t = da / (da - db);
                AddUnique(intersections, new double[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t }, tolerance * 10.0);
            }
        }
        return intersections;
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

    private static void WriteSvg(string svgPath, List<List<SamplePoint>> polylines, ThicknessResult thickness, double minU, double minV, double maxU, double maxV, double planeY)
    {
        int width = 1400;
        int height = 900;
        double margin = 70.0;
        double spanU = Math.Max(maxU - minU, 1.0);
        double spanV = Math.Max(maxV - minV, 1.0);
        double scale = Math.Min((width - margin * 2.0) / spanU, (height - margin * 2.0) / spanV);
        Func<double, double> sx = delegate(double u) { return margin + (u - minU) * scale; };
        Func<double, double> sy = delegate(double v) { return height - margin - (v - minV) * scale; };
        StringBuilder sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width).Append("\" height=\"").Append(height).Append("\" viewBox=\"0 0 ").Append(width).Append(" ").Append(height).Append("\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/>");
        sb.Append("<text x=\"30\" y=\"34\" font-family=\"Arial\" font-size=\"22\" fill=\"#111\">NX section slice Y=").Append(XmlEscape(Num(planeY))).Append(" mm</text>");
        sb.Append("<text x=\"30\" y=\"58\" font-family=\"Arial\" font-size=\"14\" fill=\"#555\">Horizontal X, vertical Z. Red line marks selected minimum section-thickness candidate.</text>");
        for (int i = 0; i < polylines.Count; i++)
        {
            if (polylines[i].Count < 2) { continue; }
            sb.Append("<polyline fill=\"none\" stroke=\"#222\" stroke-width=\"1\" points=\"");
            for (int j = 0; j < polylines[i].Count; j++)
            {
                if (j > 0) { sb.Append(" "); }
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
            double lx = (x1 + x2) / 2.0 + 10.0;
            double ly = (y1 + y2) / 2.0 - 10.0;
            sb.Append("<line x1=\"").Append(Num(x1)).Append("\" y1=\"").Append(Num(y1)).Append("\" x2=\"").Append(Num(x2)).Append("\" y2=\"").Append(Num(y2)).Append("\" stroke=\"#d71920\" stroke-width=\"3\"/>");
            sb.Append("<circle cx=\"").Append(Num(x1)).Append("\" cy=\"").Append(Num(y1)).Append("\" r=\"4\" fill=\"#d71920\"/>");
            sb.Append("<circle cx=\"").Append(Num(x2)).Append("\" cy=\"").Append(Num(y2)).Append("\" r=\"4\" fill=\"#d71920\"/>");
            sb.Append("<rect x=\"").Append(Num(lx - 4)).Append("\" y=\"").Append(Num(ly - 18)).Append("\" width=\"170\" height=\"25\" fill=\"#fff\" stroke=\"#d71920\"/>");
            sb.Append("<text x=\"").Append(Num(lx)).Append("\" y=\"").Append(Num(ly)).Append("\" font-family=\"Arial\" font-size=\"16\" fill=\"#d71920\">min ").Append(XmlEscape(Num(thickness.Distance))).Append(" mm</text>");
        }
        sb.Append("</svg>");
        File.WriteAllText(svgPath, sb.ToString(), Encoding.UTF8);
    }

    private static string ThicknessJson(ThicknessResult r)
    {
        if (r == null || !r.Found) { return "{\"found\":false}"; }
        return "{\"found\":true,\"method\":\"" + JsonEscape(r.Method) + "\",\"thickness_mm\":" + Num(r.Distance)
            + ",\"point_a\":" + PointJson(r.A) + ",\"point_b\":" + PointJson(r.B) + "}";
    }

    private static string BodiesJson(Body[] bodies)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("[");
        for (int i = 0; i < bodies.Length; i++)
        {
            if (i > 0) { sb.Append(","); }
            string name = "";
            string id = "";
            try { name = bodies[i].Name; } catch { }
            try { id = bodies[i].JournalIdentifier; } catch { }
            sb.Append("{\"index\":").Append(i)
                .Append(",\"name\":\"").Append(JsonEscape(name)).Append("\"")
                .Append(",\"journal_id\":\"").Append(JsonEscape(id)).Append("\"")
                .Append("}");
        }
        sb.Append("]");
        return sb.ToString();
    }

    private static string PointJson(SamplePoint p)
    {
        if (p == null) { return "null"; }
        return "{\"segment_index\":" + p.SegmentIndex + ",\"point_index\":" + p.PointIndex
            + ",\"body_index\":" + p.BodyIndex
            + ",\"body_name\":\"" + JsonEscape(p.BodyName) + "\""
            + ",\"body_journal_id\":\"" + JsonEscape(p.BodyJournalId) + "\""
            + ",\"xyz_mm\":[" + Num(p.X) + "," + Num(p.Y) + "," + Num(p.Z) + "]"
            + ",\"section_uv_mm\":[" + Num(p.U) + "," + Num(p.V) + "]}";
    }

    private static void SectionBasis(double[] normal, double[] basisU, double[] basisV)
    {
        if (Math.Abs(normal[0]) < 0.000001 && Math.Abs(Math.Abs(normal[1]) - 1.0) < 0.000001 && Math.Abs(normal[2]) < 0.000001)
        {
            basisU[0] = 1.0; basisU[1] = 0.0; basisU[2] = 0.0;
            basisV[0] = 0.0; basisV[1] = 0.0; basisV[2] = normal[1] >= 0.0 ? 1.0 : -1.0;
            return;
        }
        double[] reference = Math.Abs(normal[2]) < 0.9 ? new double[] { 0.0, 0.0, 1.0 } : new double[] { 1.0, 0.0, 0.0 };
        Cross(reference, normal, basisU);
        Normalize(basisU);
        Cross(normal, basisU, basisV);
        Normalize(basisV);
    }

    private static double SignedDistance(double[] point, double[] origin, double[] normal)
    {
        return (point[0] - origin[0]) * normal[0] + (point[1] - origin[1]) * normal[1] + (point[2] - origin[2]) * normal[2];
    }

    private static void AddUnique(List<double[]> points, double[] candidate, double tolerance)
    {
        double t2 = tolerance * tolerance;
        for (int i = 0; i < points.Count; i++)
        {
            double dx = points[i][0] - candidate[0];
            double dy = points[i][1] - candidate[1];
            double dz = points[i][2] - candidate[2];
            if (dx * dx + dy * dy + dz * dz <= t2) { return; }
        }
        points.Add(new double[] { candidate[0], candidate[1], candidate[2] });
    }

    private static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
    private static void Cross(double[] a, double[] b, double[] r) { r[0] = a[1] * b[2] - a[2] * b[1]; r[1] = a[2] * b[0] - a[0] * b[2]; r[2] = a[0] * b[1] - a[1] * b[0]; }
    private static bool Normalize(double[] v) { double l = Math.Sqrt(Dot(v, v)); if (l <= 0.0000001) { return false; } v[0] /= l; v[1] /= l; v[2] /= l; return true; }
    private static bool Normalize2D(ref double u, ref double v) { double l = Math.Sqrt(u * u + v * v); if (l <= 0.0000001) { return false; } u /= l; v /= l; return true; }
    private static string Num(double value) { return value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture); }
    private static string JsonEscape(string value) { return value == null ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n"); }
    private static string XmlEscape(string value) { return value == null ? "" : value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;"); }
    private static string SafeMessage(Exception ex) { return ex == null ? "Unknown exception" : ex.GetType().FullName + ": " + ex.Message; }
    public static int GetUnloadOption(string arg) { return System.Convert.ToInt32(Session.LibraryUnloadOption.Immediately); }
}
