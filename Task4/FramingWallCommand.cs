using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
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
        private const double StudOffset = 0.20;  // spacing in feet
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

                XYZ studPoint = wallCurve.Evaluate(distanceOnCurve / wallLenght, true);
                CreateVerticalStud(doc, studPoint, wallHeight, wallNormal, wallWidth, baseZelevation, wallDir);

            }


            //Create bottom and top studs
            CreateHorizontalStud(doc, wallCurve, baseZelevation, wallNormal, wallWidth); // Bottom
            CreateHorizontalStud(doc, wallCurve, baseZelevation + wallHeight, wallNormal, wallWidth); // Top

            //Create outer studs (start and end)
            CreateVerticalStud(doc, wallCurve.GetEndPoint(0), wallHeight, wallNormal, wallWidth, baseZelevation, wallDir);
            CreateVerticalStud(doc, wallCurve.GetEndPoint(1), wallHeight, wallNormal, wallWidth, baseZelevation, wallDir);

            // geting the doors and widnows hosted by that wall

            var openingElements = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                                            .OfCategory(BuiltInCategory.OST_Doors)
                                            .Concat(new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                                            .OfCategory(BuiltInCategory.OST_Windows))
                                            .Cast<FamilyInstance>()
                                            .Where(f => f.Host.Id == wallElement.Id)
                                            .ToList();

            foreach (var element in openingElements)
            {
                CreateOpeiningFraming(doc, element, wallNormal, wallWidth);
            }

        }

        private void CreateVerticalStud(Document doc, XYZ studPoint, double wallHeight, XYZ wallNormal
                                                        , double wallWidth, double baseZelevation, XYZ wallDir)
        {
            XYZ offsetBasePoint = studPoint + wallNormal * wallWidth * 0.5 + XYZ.BasisZ * baseZelevation;
            XYZ topPoint = offsetBasePoint + XYZ.BasisZ * wallHeight;
            Line studVrLine = Line.CreateBound(offsetBasePoint, topPoint);

            double offsetDist = UnitUtils.ConvertToInternalUnits(StudOffset, UnitTypeId.Feet) * 0.5;
            Transform offset1 = Transform.CreateTranslation(wallDir * offsetDist);
            Transform offset2 = Transform.CreateTranslation(wallDir * -offsetDist);

            Curve stud1 = studVrLine.CreateTransformed(offset1);
            Curve stud2 = studVrLine.CreateTransformed(offset2);

            CreateModelCurve(doc, stud1, wallNormal, stud1.GetEndPoint(0));
            CreateModelCurve(doc, stud2, wallNormal, stud2.GetEndPoint(0));
        }

        private void CreateHorizontalStud(Document doc, Curve wallCurve, double baseZelevation
                                            , XYZ wallNormal, double wallWidth)
        {
            var HrStdOffset = Transform.CreateTranslation(wallNormal * wallWidth * 0.5 + XYZ.BasisZ * baseZelevation);
            var HrStud = wallCurve.CreateTransformed(HrStdOffset);

            double offsetDist = UnitUtils.ConvertToInternalUnits(StudOffset, UnitTypeId.Feet) * 0.5;
            Transform offset1 = Transform.CreateTranslation(XYZ.BasisZ * offsetDist);
            Transform offset2 = Transform.CreateTranslation(XYZ.BasisZ * -offsetDist);

            Curve stud1 = HrStud.CreateTransformed(offset1);
            Curve stud2 = HrStud.CreateTransformed(offset2);

            CreateModelCurve(doc, stud1, wallNormal, stud1.GetEndPoint(0));
            CreateModelCurve(doc, stud2, wallNormal, stud2.GetEndPoint(0));
        }


        private void CreateOpeiningFraming(Document doc, FamilyInstance element, XYZ wallNormal, double wallWidth)
        {
            var instanceBb = element.get_BoundingBox(null);
            if (instanceBb == null) { return; }

            XYZ BbMin = instanceBb.Min;
            XYZ BbMax = instanceBb.Max;

            var normalOffset = wallNormal * wallWidth * 0.5;

            XYZ bottomLeft = BbMin + normalOffset;
            XYZ topLeft = new XYZ(BbMin.X, BbMin.Y, BbMax.Z) + normalOffset;
            XYZ topRight = new XYZ(BbMax.X, BbMin.Y, BbMax.Z) + normalOffset;
            XYZ bottomRight = new XYZ(BbMax.X, BbMin.Y, BbMin.Z) + normalOffset;


            double offsetDist = UnitUtils.ConvertToInternalUnits(StudOffset, UnitTypeId.Feet) * 0.5;

            // Vertical studs 
            XYZ openingDir = (topRight - topLeft).Normalize();
            XYZ offsetDir = openingDir.CrossProduct(wallNormal).Normalize(); 

            Transform vertOffset1 = Transform.CreateTranslation(offsetDir * offsetDist);
            Transform vertOffset2 = Transform.CreateTranslation(offsetDir * -offsetDist);

            Curve leftStud1 = Line.CreateBound(bottomLeft, topLeft).CreateTransformed(vertOffset1);
            Curve leftStud2 = Line.CreateBound(bottomLeft, topLeft).CreateTransformed(vertOffset2);

            Curve rightStud1 = Line.CreateBound(bottomRight, topRight).CreateTransformed(vertOffset1);
            Curve rightStud2 = Line.CreateBound(bottomRight, topRight).CreateTransformed(vertOffset2);

            CreateModelCurve(doc, leftStud1, wallNormal, leftStud1.GetEndPoint(0));
            CreateModelCurve(doc, leftStud2, wallNormal, leftStud2.GetEndPoint(0));
            CreateModelCurve(doc, rightStud1, wallNormal, rightStud1.GetEndPoint(0));
            CreateModelCurve(doc, rightStud2, wallNormal, rightStud2.GetEndPoint(0));

            Transform horizOffset1 = Transform.CreateTranslation(XYZ.BasisZ * offsetDist);
            Transform horizOffset2 = Transform.CreateTranslation(XYZ.BasisZ * -offsetDist);

            Curve topStud1 = Line.CreateBound(topLeft, topRight).CreateTransformed(horizOffset1);
            Curve topStud2 = Line.CreateBound(topLeft, topRight).CreateTransformed(horizOffset2);

            CreateModelCurve(doc, topStud1, wallNormal, topStud1.GetEndPoint(0));
            CreateModelCurve(doc, topStud2, wallNormal, topStud2.GetEndPoint(0));

            // Bottom stud for windows
            var InstanceCategory = element.Category.Id.IntegerValue;
            if (InstanceCategory == (int)BuiltInCategory.OST_Windows)
            {
                Curve bottomStud1 = Line.CreateBound(bottomLeft, bottomRight).CreateTransformed(horizOffset1);
                Curve bottomStud2 = Line.CreateBound(bottomLeft, bottomRight).CreateTransformed(horizOffset2);
                CreateModelCurve(doc, bottomStud1, wallNormal, bottomStud1.GetEndPoint(0));
                CreateModelCurve(doc, bottomStud2, wallNormal, bottomStud2.GetEndPoint(0));
            }
        }

        private void CreateModelCurve(Document doc, Curve studCurve, XYZ wallNormal, XYZ wallOrigin)
        {
            Plane plane = Plane.CreateByNormalAndOrigin(wallNormal, wallOrigin);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
            doc.Create.NewModelCurve(studCurve, sketchPlane);
        }
    }
}
