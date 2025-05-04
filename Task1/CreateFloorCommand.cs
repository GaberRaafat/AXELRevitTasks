using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Task1
{
    [Transaction(TransactionMode.Manual)]
    public class CreateFloorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {

                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;

                #region Floor retrive
                var Drawingfloor = new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                                           .Cast<FloorType>().FirstOrDefault();
                if (Drawingfloor == null)
                {
                    message = "No Floors have been found in the projec";
                    return Result.Failed;
                }
                #endregion

                #region Level retrive
                var DrawingLevel = new FilteredElementCollector(doc).OfClass(typeof(Level))
                                            .Cast<Level>().FirstOrDefault();

                if (DrawingLevel == null)
                {
                    message = "No Levels have been found in the projec";
                    return Result.Failed;
                }
                #endregion

                List<XYZ> PointList = new List<XYZ>
                {
                    new XYZ(0, 0, 0) ,new XYZ(79, 0, 0),
                    new XYZ(44, 25, 0), new XYZ(13, 25, 0),
                    new XYZ(13, 40, 0), new XYZ(-8, 40, 0),
                    new XYZ(55, 34, 0), new XYZ(55, 10, 0),
                    new XYZ(79, 34, 0), new XYZ(55, 34, 0),
                    new XYZ(0, 20, 0), new XYZ(0, 0, 0),
                    new XYZ(55, 10, 0), new XYZ(44, 12, 0),
                    new XYZ(-8, 40, 0), new XYZ(-8, 20, 0),
                    new XYZ(79, 0, 0), new XYZ(79, 34, 0),
                    new XYZ(44, 12, 0), new XYZ(44, 25, 0),
                    new XYZ(-8, 20, 0), new XYZ(0, 20, 0),
                    new XYZ(13, 25, 0), new XYZ(13, 40, 0)
                };

                List<Curve> Floorcurves = CreateListOfCurve(PointList);

                CurveLoop FloorCurveLoop = new CurveLoop();

                if (!CheckCurveLoopCreationAbility(Floorcurves))
                {
                    List<XYZ> newArrangedList = ArrangePoints(PointList);

                    List<Curve> newListOfCurv = CreateListOfCurve(newArrangedList);

                    if (!CheckCurveLoopCreationAbility(newListOfCurv))
                    {
                        TaskDialog.Show("Error", "The floor element can not be created from the gived list of points");
                        return Result.Failed;
                    }

                    FloorCurveLoop = CurveLoop.Create(newListOfCurv);
                }

                if (FloorCurveLoop.Count() == 0)  
                {
                    FloorCurveLoop = CurveLoop.Create(Floorcurves);
                }


                using (Transaction transaction = new Transaction(doc, " Create floor"))
                {
                    transaction.Start();
                    Floor.Create(doc, new List<CurveLoop>() { FloorCurveLoop }, Drawingfloor.Id, DrawingLevel.Id);
                    TaskDialog.Show("Success", "Floor Created Successfully");
                    transaction.Commit();
                }
                return Result.Succeeded;

            }
            catch
            {
                TaskDialog.Show("Error", "Floor can not be created with given points ,Try another");
                return Result.Failed;
            }

        }


        public bool CheckCurveLoopCreationAbility(List<Curve> curveList)
        {
            for (int i = 0; i < curveList.Count - 1; i++)
            {
                if (!curveList[i].GetEndPoint(1).IsAlmostEqualTo(curveList[i + 1].GetEndPoint(0)))
                {
                    return false;
                }
            }
            var lastpoint = curveList.Last().GetEndPoint(1);
            var firstpoint = curveList.First().GetEndPoint(0);

            return lastpoint.IsAlmostEqualTo(firstpoint)? true : false;
        }


        public List<XYZ> ArrangePoints(List<XYZ> points)
        {

            List<(XYZ Start, XYZ End)> segments = new List<(XYZ, XYZ)>();
            for (int i = 0; i < points.Count; i += 2)
            {
                segments.Add((points[i], points[i + 1]));
            }

            List<XYZ> arrangedPoints = new List<XYZ>();
            var current = segments[0];
            arrangedPoints.Add(current.Start);
            arrangedPoints.Add(current.End);
            segments.RemoveAt(0);

            while (segments.Count > 0)
            {
                XYZ lastPoint = arrangedPoints[arrangedPoints.Count - 1]; 

                for (int i = 0; i < segments.Count; i++)
                {
                    var (start, end) = segments[i];

                    if (lastPoint.IsAlmostEqualTo(start))
                    {
                        arrangedPoints.Add(end);
                        segments.RemoveAt(i);
                        break;
                    }
                    else if (lastPoint.IsAlmostEqualTo(end))
                    {
                        arrangedPoints.Add(start);
                        segments.RemoveAt(i);
                        break;
                    }

                }
            }

            return arrangedPoints;
        }


        public List<Curve> CreateListOfCurve(List<XYZ> poiintlist)
        {
            var createdListOfCurves = new List<Curve>();

            for (int i = 0; i < poiintlist.Count - 1; i++)
            {
                createdListOfCurves.Add(Line.CreateBound(poiintlist[i], poiintlist[i + 1]));
            }


            return createdListOfCurves;

        }

    }
}