using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Task4
{
    [Transaction(TransactionMode.Manual)]
    public class FramingWallCommand : IExternalCommand
    {
        private const double StudSpacing = 2.00;  // spacing in feet
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {

                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;

                var selectedWallRef = uidoc.Selection.PickObject(ObjectType.Element, "Select a wall");

                var wallElement = doc.GetElement(selectedWallRef) as Wall;

                if (wallElement == null)
                {
                    TaskDialog.Show("Error", "Select a valid wall");
                    return Result.Failed;
                }

                using (Transaction transaction = new Transaction(doc, "Create Wall Framing"))
                {
                    transaction.Start();
                    CreateFraming(doc, wallElement);
                    transaction.Commit();
                }

                TaskDialog.Show("Success", "Wall framing created successfully.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private void CreateFraming(Document doc, Wall wallElement)
        {
            var walllocationCurve = wallElement.Location as LocationCurve;

            if (walllocationCurve == null) { return; }

            var wallCurve = walllocationCurve.Curve as Curve;

            // get wall parameters 

            var wallLenght = wallCurve.Length;
            var wallWidth = wallElement.Width;
            var wallHeight = wallElement.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble();

            //  get wall base elevation 

            var levelId = wallElement.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId();
            var baseLevel = doc.GetElement(levelId) as Level;
            double baseZelevation = baseLevel.Elevation;

            // get wall normal 
            var wallDir = (wallCurve.GetEndPoint(1) - wallCurve.GetEndPoint(0)).Normalize();
            var wallNormal = wallDir.CrossProduct(XYZ.BasisZ).Normalize();

            // Create Vertical Studs
            double StdSpacing = UnitUtils.ConvertToInternalUnits(StudSpacing, UnitTypeId.Feet);
            int numberOfStud = (int)(wallLenght / StdSpacing);

            for (int i = 1; i <= numberOfStud; i++) 
            {
                var distanceOnCurve = i * StdSpacing;

                if (Math.Abs(distanceOnCurve - wallLenght) < 0.01)
                    continue;

                XYZ studPoint = wallCurve.Evaluate(distanceOnCurve/ wallLenght, true);
                CreateVerticalStud(doc, studPoint, wallHeight, wallNormal, wallWidth, baseZelevation);

            }


            //Create bottom and top studs
            CreateHorizontalStud(doc, wallCurve, baseZelevation, wallNormal, wallWidth); // Bottom
            CreateHorizontalStud(doc, wallCurve, baseZelevation + wallHeight, wallNormal, wallWidth); // Top

            //Create outer studs (start and end)
            CreateVerticalStud(doc, wallCurve.GetEndPoint(0), wallHeight, wallNormal, wallWidth, baseZelevation);
            CreateVerticalStud(doc, wallCurve.GetEndPoint(1), wallHeight, wallNormal, wallWidth, baseZelevation);

        }
        private void CreateVerticalStud(Document doc, XYZ studPoint, double wallHeight, XYZ wallNormal
                                                        , double wallWidth, double baseZelevation)
        {
            XYZ offsetBasePoint = studPoint + wallNormal * wallWidth * 0.5 + XYZ.BasisZ * baseZelevation;
            XYZ topPoint = offsetBasePoint + XYZ.BasisZ * wallHeight;

            Line studVrLine = Line.CreateBound(offsetBasePoint, topPoint);
            CreateModelCurve(doc, studVrLine, wallNormal, offsetBasePoint);
        }

        private void CreateHorizontalStud(Document doc, Curve wallCurve, double baseZelevation
                                            ,XYZ wallNormal, double wallWidth)
        {
            var HrStdOffset = Transform.CreateTranslation(wallNormal * wallWidth * 0.5 + XYZ.BasisZ * baseZelevation);

            var HrStud = wallCurve.CreateTransformed(HrStdOffset);
            CreateModelCurve(doc, HrStud, wallNormal, HrStud.GetEndPoint(0));
        }


        private void CreateModelCurve(Document doc, Curve studCurve, XYZ wallNormal, XYZ wallOrigin)
        {
            Plane plane = Plane.CreateByNormalAndOrigin(wallNormal, wallOrigin);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
            doc.Create.NewModelCurve(studCurve, sketchPlane);
        }
    }
}
