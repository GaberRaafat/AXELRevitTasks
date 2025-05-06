using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Task2
{
    [Transaction(TransactionMode.Manual)]
    public class ProperPlacementCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;

                var wallReference = uidoc.Selection.PickObject(ObjectType.Element);
                var wallElement = doc.GetElement(wallReference) as Wall;
                var wallLocation = wallElement.Location as LocationCurve;
                if (wallLocation == null)
                {
                    return Result.Failed;
                }
                double wallThickness = wallElement.Width;

                List<Room> bathroomRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .OfClass(typeof(SpatialElement))
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Name.Contains("Bathroom"))
                    .ToList();

                List<(XYZ point, XYZ normal, XYZ doorLocation)> WCInsertionPoints = new List<(XYZ, XYZ, XYZ)>();

                foreach (var room in bathroomRooms)
                {
                    var boundaries = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                    List<ElementId> boundaryWallIds = new List<ElementId>();

                    foreach (var segmentList in boundaries)
                    {
                        foreach (var segment in segmentList)
                        {
                            Element boundaryElement = doc.GetElement(segment.ElementId);
                            if (boundaryElement is Wall)
                            {
                                boundaryWallIds.Add(boundaryElement.Id);
                            }
                        }
                    }

                    List<FamilyInstance> doors = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .Where(d => boundaryWallIds.Contains(d.Host?.Id))
                        .ToList();

                    if (!doors.Any())
                    {
                        continue;
                    }

                    var doorLocation = doors.First().GetTransform().Origin;

                    foreach (var segmentList in boundaries)
                    {
                        foreach (var segment in segmentList)
                        {
                            if (segment.ElementId != wallElement.Id)
                            {
                                continue;
                            }

                            Curve wallSegmentCurve = segment.GetCurve();
                            XYZ segmentStart = wallSegmentCurve.GetEndPoint(0);
                            XYZ segmentEnd = wallSegmentCurve.GetEndPoint(1);
                            XYZ segmentDir = (segmentEnd - segmentStart).Normalize();
                            XYZ segmentNormal = segmentDir.CrossProduct(XYZ.BasisZ).Normalize();

                            XYZ midpoint = (segmentStart + segmentEnd) / 2;
                            XYZ inwardTestPoint = midpoint - segmentNormal * 0.5;
                            XYZ outwardTestPoint = midpoint + segmentNormal * 0.5;

                            bool inwardInRoom = room.IsPointInRoom(inwardTestPoint);
                            bool outwardInRoom = room.IsPointInRoom(outwardTestPoint);
                            XYZ finalNormal = inwardInRoom ? segmentNormal : (outwardInRoom ? -segmentNormal : null);

                            if (finalNormal == null) continue;

                            double distToStart = doorLocation.DistanceTo(segmentStart);
                            double distToEnd = doorLocation.DistanceTo(segmentEnd);

                            XYZ placementPoint = distToStart > distToEnd ? segmentStart : segmentEnd;

                            XYZ finalPoint;
                            double segmentLength = wallSegmentCurve.Length;
                            if (segmentLength < 1.5)
                            {
                                finalPoint = (segmentStart + segmentEnd) / 2;
                            }
                            else
                            {
                                XYZ displacementDirection = (placementPoint.IsAlmostEqualTo(segmentStart)) ? segmentDir : -segmentDir;
                                finalPoint = placementPoint + displacementDirection * 1.5;
                            }

                            WCInsertionPoints.Add((finalPoint, finalNormal, doorLocation));
                        }
                    }
                }

                FamilySymbol WCfamilySymbol = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(f => f.Name == "ADA");

                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => l.Name == "Level 1");

                if (WCfamilySymbol == null || level == null) return Result.Failed;

                using (Transaction transaction = new Transaction(doc, "Family insertion"))
                {
                    transaction.Start();

                    if (!WCfamilySymbol.IsActive)
                    {
                        WCfamilySymbol.Activate();
                        doc.Regenerate();
                    }

                    foreach (var (point, normal, doorLocation) in WCInsertionPoints)
                    {
                        FamilyInstance instance = doc.Create.NewFamilyInstance(
                            location: point,
                            level: level,
                            symbol: WCfamilySymbol,
                            host: wallElement,
                            structuralType: StructuralType.NonStructural
                        );

                        XYZ directionToDoor = (doorLocation - point).Normalize();
                        XYZ wallPlaneDirection = directionToDoor - (directionToDoor.DotProduct(XYZ.BasisZ) * XYZ.BasisZ);
                        wallPlaneDirection = wallPlaneDirection.Normalize();

                        double dotProduct = wallPlaneDirection.DotProduct(normal);
                        if (dotProduct > 0)
                        {
                            instance.flipFacing();
                        }
                    }
                    transaction.Commit();
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}