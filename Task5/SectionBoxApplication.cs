using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Task5
{

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SectionBoxApplication : IExternalApplication
    {
        public static List<ViewSectionData> waitingViewSections = new List<ViewSectionData>();
        private UIApplication uiapp;

        public Result OnStartup(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentChanged += OnDocumentChanged;
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
            if (uiapp != null)
                uiapp.Idling -= OnIdLing;
            return Result.Succeeded;
        }

        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            var doc = e.GetDocument();
            var activeView = doc.ActiveView;

            if (activeView.ViewType != ViewType.FloorPlan || activeView.GenLevel == null)
                return;

            Level level = activeView.GenLevel;
            double viewlevelElevation = level.Elevation;

            var generatedElementsId = e.GetAddedElementIds();

            foreach (var elementId in generatedElementsId)
            {
                var elementadded = doc.GetElement(elementId);

                if (elementadded is ViewSection)
                {
                    ViewSectionData viewSectionData = new ViewSectionData
                    {
                        sectionId = elementId,
                        LevelElevation = viewlevelElevation
                    };
                    waitingViewSections.Add(viewSectionData);
                }
            }

            if (waitingViewSections.Count > 0)
            {
                uiapp = new UIApplication(doc.Application);
                uiapp.Idling += OnIdLing;
            }
        }

        private void OnIdLing(object sender, IdlingEventArgs e)
        {
            UIApplication app = sender as UIApplication;
            app.Idling -= OnIdLing;
            var uidoc = app?.ActiveUIDocument;

            if (uidoc == null)
            {
                return;
            }

            var doc = uidoc.Document;

            double offset = 10;

            using (Transaction transaction = new Transaction(doc, "Modify Section Boxes"))
            {
                transaction.Start();
                try
                {
                    foreach (var viewSec in waitingViewSections)
                    {
                        var viewSection = doc.GetElement(viewSec.sectionId) as ViewSection;
                        if (viewSection == null || !viewSection.CropBoxActive) continue;

                        var sectionCB = viewSection.CropBox;

                        var newBb = new BoundingBoxXYZ
                        {
                            Min = new XYZ(sectionCB.Min.X, sectionCB.Min.Y, viewSec.LevelElevation - offset),
                            Max = new XYZ(sectionCB.Max.X, sectionCB.Max.Y, viewSec.LevelElevation + offset)
                        };

                        viewSection.CropBox = newBb;
                        viewSection.CropBoxActive = true;
                        viewSection.CropBoxVisible = true;
                    }

                    TaskDialog.Show("Success", "Section Boxes Modified Successfully.");
                }
                catch (Exception ex)
                {
                    transaction.RollBack();
                    TaskDialog.Show("Error", ex.Message);
                    return;
                }

                transaction.Commit();
            }
            waitingViewSections.Clear();
        }
    }
}

