using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;

namespace Task5
{
    public class ViewSectionData
    {
        public ElementId sectionId { get; set; }
        public double LevelElevation { get; set; }
    }
}
