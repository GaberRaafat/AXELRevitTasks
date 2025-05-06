using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace Task3
{
    [Transaction(TransactionMode.Manual)]
    public class AddingThresholdCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;

                var floorType = new FilteredElementCollector(doc)
                                .OfClass(typeof(FloorType))
                                .Cast<FloorType>()
                                .FirstOrDefault();

                var allRooms = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_Rooms)
                                .OfClass(typeof(SpatialElement))
                                .Cast<Room>()
                                .ToList();

                var allDoors = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_Doors)
                                .WhereElementIsNotElementType()
                                .OfType<FamilyInstance>()
                                .ToList();

                using (Transaction transaction = new Transaction(doc, "Create Room & Threshold Floors"))
                {
                    transaction.Start();

                    int roomFloorsCreated = 0;
                    int thresholdFloorsCreated = 0;

                    foreach (Room room in allRooms)
                    {
                        if (room.LevelId == ElementId.InvalidElementId) continue;
                        Level roomLevel = doc.GetElement(room.LevelId) as Level;
                        if (roomLevel == null) continue;

                        // Create Room Floor
                        var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                        if (boundaries == null) continue;

                        foreach (var segmentList in boundaries)
                        {
                            List<Curve> boundaryCurves = new List<Curve>();
                            foreach (var segment in segmentList)
                            {
                                boundaryCurves.Add(segment.GetCurve());
                            }

                            if (boundaryCurves.Count > 2)
                            {
                                var loop = CurveLoop.Create(boundaryCurves);
                                Floor.Create(doc, new List<CurveLoop> { loop }, floorType.Id, roomLevel.Id);
                                roomFloorsCreated++;
                            }
                        }

                        // Create Threshold Floors
                        var doorsInRoom = GetDoorsInRoom(doc, room, allDoors);
                        foreach (var threshold in CreateThresholds(doc, room, doorsInRoom))
                        {
                            Floor.Create(doc, new List<CurveLoop> { CurveLoop.Create(threshold) }, floorType.Id, roomLevel.Id);
                            thresholdFloorsCreated++;
                        }
                    }

                    transaction.Commit();

                    TaskDialog.Show("Success",
                        $"{roomFloorsCreated} room floors created.\n{thresholdFloorsCreated} threshold floors created.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private List<FamilyInstance> GetDoorsInRoom(Document doc, Room room, List<FamilyInstance> doors)
        {
            List<FamilyInstance> result = new List<FamilyInstance>();
            foreach (var door in doors)
            {
                if ((door.FromRoom?.Id == room.Id) || (door.ToRoom?.Id == room.Id))
                    result.Add(door);
            }
            return result;
        }

        private List<List<Curve>> CreateThresholds(Document doc, Room room, List<FamilyInstance> doors)
        {
            List<List<Curve>> allThresholds = new List<List<Curve>>();

            BoundingBoxXYZ roombb = room.get_BoundingBox(null);

            if (roombb == null) return allThresholds;

            XYZ roomCenter = (roombb.Min + roombb.Max) / 2;

            foreach (var door in doors)
            {
                try
                {
                    if (!(door.Host is Wall hostWall)) continue;

                    double wallWidth = hostWall.Width;
                    double doorWidth = 3.0;

                    var doorType = doc.GetElement(door.GetTypeId());
                    var widthParam = doorType?.LookupParameter("Width");
                    if (widthParam != null && widthParam.HasValue)
                        doorWidth = widthParam.AsDouble();

                    var location = door.Location as LocationPoint;
                    if (location == null) continue;

                    XYZ doorCenter = location.Point;
                    XYZ facing = door.FacingOrientation.Normalize();
                    XYZ toRoomDir = (roomCenter - doorCenter).Normalize();

                    XYZ normal = facing.DotProduct(toRoomDir) > 0 ? facing : facing.Negate();
                    XYZ right = new XYZ(normal.Y, -normal.X, 0);

                    double halfDoorWidth = doorWidth / 2;
                    double halfThresholdDepth = wallWidth / 2;

                    XYZ bl = doorCenter - right * halfDoorWidth - normal * halfThresholdDepth;
                    XYZ tl = doorCenter - right * halfDoorWidth;
                    XYZ tr = doorCenter + right * halfDoorWidth;
                    XYZ br = doorCenter + right * halfDoorWidth - normal * halfThresholdDepth;


                    var threshold = new List<Curve>
                    {
                        Line.CreateBound(bl, tl),
                        Line.CreateBound(tl, tr),
                        Line.CreateBound(tr, br),
                        Line.CreateBound(br, bl)
                    };

                    allThresholds.Add(threshold);
                }
                catch
                {
                    continue;
                }
            }

            return allThresholds;
        }
    }
}