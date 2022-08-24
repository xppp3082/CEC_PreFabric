#region Namespaces
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB.Structure;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using System.IO;
#endregion

namespace CEC_PreFabric
{
    //�i�{���[�c�j
    //1.�����ϥΪ̥i�H���elements
    //2.�Ы�ISOmetric
    //3.�Q�ο�ܪ�elements�ӧ���ISO��orientation

    [Transaction(TransactionMode.Manual)]
    public class CreateISO : IExternalCommand
    {
        public static DisplayUnitType unitType = DisplayUnitType.DUT_MILLIMETERS;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;
            try
            {
                //���o�չϼ˪O����
                PreFabricUI ui = new PreFabricUI();
                FilteredElementCollector coll = new FilteredElementCollector(doc);
                List<Autodesk.Revit.DB.View> templateList = new List<Autodesk.Revit.DB.View>();
                coll.OfCategory(BuiltInCategory.OST_Views);
                foreach (Element v in coll)
                {
                    Autodesk.Revit.DB.View tempView = v as Autodesk.Revit.DB.View;
                    if (tempView.IsTemplate)
                    {
                        templateList.Add(tempView);
                    }
                }
                ui.startingNumTextBox.IsReadOnly = true;
                ui.viewTemplateComboBox.ItemsSource = templateList;
                ui.ShowDialog();
                string viewName = ui.viewNameTextBox.Text;
                string levelName = ui.levelName.Text;
                string regionName = ui.regionName.Text;
                 Autodesk.Revit.DB.View selectedView = ui.viewTemplateComboBox.SelectedItem as Autodesk.Revit.DB.View;
                //MessageBox.Show(selectedView.Name);

                //����ޤ���-�����ե��a��
                ISelectionFilter pipeFilter = new PipeSelectionFilter(doc);
                ISelectionFilter linkedPipeFilter = new linkedPipeSelectionFilter(doc);
                IList<Reference> pickPipeRefs = uidoc.Selection.PickObjects(ObjectType.Element, pipeFilter, "�п�ܭn�s�@�b���Ϫ��ޤ���");
                List<Element> pickPipes = new List<Element>();

                foreach (Reference refer in pickPipeRefs)
                {
                    Element tempPipe = doc.GetElement(refer);
                    pickPipes.Add(tempPipe);
                }

                using (TransactionGroup transGroup = new TransactionGroup(doc))
                {
                    transGroup.Start("���͹w�sISO��");
                    //step1�в���ISO�Ϩç�ISO���ܬ���e����
                    Autodesk.Revit.DB.View isoView = createNewViewAndZoom(commandData);

                    //step2 - �ܧ���ϰŵ��d�� (�ݬO�_�ݭn�]�w�孱��)
                    using (Transaction trans = new Transaction(doc))
                    {
                        trans.Start("�ܧ���ϰŵ���");
                        BoundingBoxXYZ bounding = isoView.CropBox;
                        Transform transform = bounding.Transform;
                        Transform transInverse = transform.Inverse;
                        List<XYZ> points = new List<XYZ>();
                        XYZ ptWork = null;
                        //�N�@�ɮy���ഫ�����Ϯy�СA�z�LTransformInverse���f�ܨӧ���
                        foreach (Reference r in pickPipeRefs)
                        {
                            BoundingBoxXYZ bb = doc.GetElement(r).get_BoundingBox(null);
                            ptWork = transInverse.OfPoint(bb.Min);
                            points.Add(ptWork);
                            ptWork = transInverse.OfPoint(bb.Max);
                            points.Add(ptWork);
                        }
                        double adjust = 500;
                        double adjustOffset = UnitUtils.ConvertToInternalUnits(adjust, unitType);
                        BoundingBoxXYZ sb = new BoundingBoxXYZ();
                        sb.Min = new XYZ(points.Min(p => p.X - adjustOffset),
                                          points.Min(p => p.Y - adjustOffset),
                                          points.Min(p => p.Z - adjustOffset));
                        sb.Max = new XYZ(points.Max(p => p.X + adjustOffset),
                                       points.Max(p => p.Y + adjustOffset),
                                       points.Max(p => p.Z + adjustOffset));
                        isoView.CropBox = sb;
                        isoView.CropBoxActive = false;
                        isoView.CropBoxVisible = false;
                        isoView.DetailLevel = ViewDetailLevel.Fine;
                        isoView.DisplayStyle = DisplayStyle.FlatColors;

                        //�ܧ�SectionBox�����A
                        //isoView3D.GetSectionBox();
                        List<XYZ> boundingCorners = new List<XYZ>();
                        List<double> boundingX = new List<double>();
                        List<double> boundingY = new List<double>();
                        List<double> boundingZ = new List<double>();
                        foreach (Element e in pickPipes)
                        {
                            BoundingBoxXYZ tempBox = e.get_BoundingBox(null);
                            XYZ maxCorner = tempBox.Max;
                            XYZ minCorner = tempBox.Min;
                            boundingCorners.Add(maxCorner);
                            boundingCorners.Add(minCorner);

                            //���XYZ�Ȥ���[�J
                            boundingX.Add(maxCorner.X);
                            boundingX.Add(minCorner.X);
                            boundingY.Add(maxCorner.Y);
                            boundingY.Add(minCorner.Y);
                            boundingZ.Add(maxCorner.Z);
                            boundingZ.Add(minCorner.Z);
                        }
                        boundingX = boundingX.OrderByDescending(x => x).ToList();
                        boundingY = boundingY.OrderByDescending(y => y).ToList();
                        boundingZ = boundingZ.OrderByDescending(z => z).ToList();
                        XYZ maxBB = new XYZ(boundingX.First(), boundingY.First(), boundingZ.First());
                        XYZ minBB = new XYZ(boundingX.Last(), boundingY.Last(), boundingZ.Last());

                        boundingCorners = boundingCorners.OrderByDescending(x => x.X).ThenByDescending(x => x.X).ThenByDescending(x => x.Y).ToList();
                        BoundingBoxXYZ newBox = new BoundingBoxXYZ();
                        newBox.Max = maxBB;
                        newBox.Min = minBB;
                        //�ন3D���ϫ�~�i�H��w����
                        View3D isoView3D = isoView as View3D;
                        isoView3D.SaveOrientationAndLock();
                        isoView3D.IsSectionBoxActive = true;
                        isoView3D.SetSectionBox(newBox);
                        isoView3D.Name = viewName;
                        isoView3D.ViewTemplateId = selectedView.Id;
                        trans.Commit();
                    }

                    //step3 - �w��S�����[�J�@�ΰѼơA�å[�H���ռg�J�Ʀr
                    #region ���n���J�@�ΰѼƪ��~��
                    //1.�n���T�{�o��binding�O�_�s�b
                    //2.�T�{�Q�g�J���~���̦��S���o�ӰѼơA�p�G���h�h���o�ӫ~��
                    //3.�N�ѤU���~���g�J�J��binding
                    //string spName = "�ޮƵ����s��";
                    //string spFullName = "�����Ƹ�";
                    List<string> paraName = new List<string>() { "�ޮƵ����s��","�����Ƹ�"};
                    foreach(string st in paraName)
                    {
                        List<Category> defaultCateList = new List<Category>()
                {
                    Category.GetCategory(doc,BuiltInCategory.OST_PipeCurves),
                    Category.GetCategory(doc,BuiltInCategory.OST_DuctCurves),
                    Category.GetCategory(doc,BuiltInCategory.OST_Conduit)
                };
                        CategorySet catSet = app.Create.NewCategorySet();
                        foreach (Element e in pickPipes)
                        {
                            Category tempCate = e.Category;
                            if (!catSet.Contains(tempCate))
                            {
                                catSet.Insert(tempCate);
                            }
                        }
                        BindingMap bm = doc.ParameterBindings;
                        DefinitionBindingMapIterator itor = bm.ForwardIterator();
                        itor.Reset();
                        Definition d = null;
                        ElementBinding elemBind = null;
                        //�p�G�{�b���M�פ��w�g���J�ӰѼ����A�h���ݭ��s���J
                        while (itor.MoveNext())
                        {
                            d = itor.Key;
                            if (d.Name == st)
                            {
                                elemBind = (ElementBinding)itor.Current;
                                break;
                            }
                        }

                        //�p�G�Ӧ@�ΰѼƤw�g���J�����M�װѼơA���s�[�Jbinding
                        if (d.Name == st && catSet.Size > 0)
                        {
                            using (Transaction tx = new Transaction(doc, "Add Binding"))
                            {
                                tx.Start();
                                InstanceBinding ib = doc.Application.Create.NewInstanceBinding(catSet);
                                bool result = doc.ParameterBindings.ReInsert(d, ib, BuiltInParameterGroup.PG_SEGMENTS_FITTINGS);
                                tx.Commit();
                            }
                        }
                        //�p�G�ӱM�װѼ��٨S�Q���J�A�h���J��
                        else if (d.Name != st)
                        {
                            //MessageBox.Show($"�M�ש|�����J�u {spName}�v �ѼơA�N�۰ʸ��J");
                            MessageBox.Show($"�M�ש|�����J�u {st}�v �ѼơA�N�۰ʸ��J");
                            var infoPath = @"Dropbox\info.json";
                            var jsonPath = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), infoPath);
                            if (!File.Exists(jsonPath)) jsonPath = Path.Combine(Environment.GetEnvironmentVariable("AppData"), infoPath);
                            if (!File.Exists(jsonPath)) throw new Exception("�Цw�˨õn�JDropbox�ୱ���ε{��!");
                            var dropboxPath = File.ReadAllText(jsonPath).Split('\"')[5];
                            var spFilePath = dropboxPath + @"\BIM-Share\BIM�@�ΰѼ�.txt";
                            app.SharedParametersFilename = spFilePath;
                            DefinitionFile spFile = app.OpenSharedParameterFile();
                            ExternalDefinition targetDefinition = null;
                            foreach (DefinitionGroup dG in spFile.Groups)
                            {
                                if (dG.Name == "���q_�w��")
                                {
                                    foreach (ExternalDefinition def in dG.Definitions)
                                    {
                                        if (def.Name == st) targetDefinition = def;
                                    }
                                }
                            }
                            //�b�����e�n�إߤ@�Ӽf�ָӰѼƬO�_�w�g�Q���J������A�p�G�w�Q���J�h�����J
                            if (targetDefinition != null)
                            {
                                using (Transaction trans = new Transaction(doc))
                                {
                                    trans.Start("���J�@�ΰѼ�");
                                    InstanceBinding newIB = app.Create.NewInstanceBinding(catSet);
                                    doc.ParameterBindings.Insert(targetDefinition, newIB, BuiltInParameterGroup.PG_SEGMENTS_FITTINGS);
                                    trans.Commit();
                                }
                            }
                        }
                        #endregion
                        //step4 - �w����Ϥ����ާ��[�Wtag�A���ըäW�J�s��-->���ժ��g�k�ӫ��g�٫ݫ��
                        int keyToSet = 1;
                        List<Element> pipeListToCheck = pickPipes; //�s�W�t�~�@��List�h����e���A�Q�ΥL�ӹ��pickPipes��������A�H�ΧR���w�g�t��쪺
                        List<Element> alreadySetList = new List<Element>(); //�s�W�@��List�h�`���w�g�s���L��
                        Element tempElem = null;
                        string outPut = "";
                        foreach (Element e in pickPipes)
                        {
                            List<Element> sameList = new List<Element>();
                            tempElem = e;
                            //��������-->����(�W��)�B�ޮ|(�j�p)�B����
                            string elemName = e.Name;
                            string elemSize = getPipeDiameter(e);
                            double elemLength = Math.Round(e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble(), 2);
                            //string elemFabName = e.LookupParameter(spName).AsString();
                            //if (elemFabName != "")
                            //{
                            //    MessageBox.Show("�襤�ޥ󤤤w�g�J�����s���A�вM���s����A���s�g�J");
                            //    return Result.Failed;
                            //}
                            foreach (Element ee in pickPipes)
                            {
                                double tempLength = Math.Round(ee.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble(), 2);
                                if (elemName == ee.Name && elemSize == getPipeDiameter(ee) && !alreadySetList.Contains(ee) && elemLength == tempLength/*&& ee.LookupParameter(spName).AsString() == ""*/)
                                {
                                    sameList.Add(ee);
                                    alreadySetList.Add(ee);
                                }
                            }

                            if (sameList.Count() > 0)
                            {
                                outPut += $"�P�W��{elemName}�A�B�j�p�P��{elemSize}���ާ��@��{sameList.Count()}��\n";
                                using (Transaction trans = new Transaction(doc))
                                {
                                    trans.Start("�g�J�����s��");
                                    foreach (Element p in sameList)
                                    {
                                        Parameter fabNum = p.LookupParameter(paraName[0]);
                                        Parameter fabFullName = p.LookupParameter(paraName[1]);
                                        string systemName = p.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM).AsString();
                                        string numToSet = systemName + "-" + levelName + "-" + regionName + "-" + keyToSet.ToString();
                                        fabNum.Set(keyToSet.ToString());
                                        fabFullName.Set(numToSet);
                                    }
                                    keyToSet += 1;
                                    trans.Commit();
                                }
                            }
                        }
                    }

                    //�w��C�Ӥ����W�h���~������
                    Element multiCateTag = findMultiCateTag(doc);
                    using (Transaction trans = new Transaction(doc))
                    {
                        trans.Start("��m�h���~������");
                        foreach (Element e in pickPipes)
                        {
                            Reference elemRefer = new Reference(e);
                            MEPCurve mepCrv = e as MEPCurve;
                            LocationCurve pipeLocate = mepCrv.Location as LocationCurve;
                            Curve pipeCrv = pipeLocate.Curve;
                            XYZ middlePt = pipeCrv.Evaluate(0.5, true);
                            IndependentTag fabricTag = IndependentTag.Create(doc, multiCateTag.Id, isoView.Id, elemRefer, true, TagOrientation.Horizontal, middlePt);
                            XYZ tagHead = fabricTag.TagHeadPosition;
                            //�]�w�H�����ͤ@�woffset�Ȫ���k
                            Line tempLine = Line.CreateBound(middlePt, tagHead);
                            XYZ targetPt = tempLine.Evaluate(0.3, true);
                            fabricTag.TagHeadPosition = targetPt;
                        }
                        trans.Commit();
                    }
                    //MessageBox.Show(outPut);
                    transGroup.Assimilate();
                }
            }
            catch
            {
                MessageBox.Show("���楢��");
                return Result.Failed;
            }

            return Result.Succeeded;
        }
        //�s�@�@�Ӥ�k���h���~������
        public Element findMultiCateTag(Document doc)
        { string tagetName = "M_�����s������_�h���~������";
            Element targetElement = null;
            FilteredElementCollector coll = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_MultiCategoryTags).WhereElementIsElementType();
            foreach(Element e in coll)
            {
                if (e.Name == tagetName) targetElement = e;
            }
            return targetElement;
        }
        public string getPipeDiameter(Element elem)
        {
            Document _doc = elem.Document;
            Category pipe = Category.GetCategory(_doc, BuiltInCategory.OST_PipeCurves);
            Category duct = Category.GetCategory(_doc, BuiltInCategory.OST_DuctCurves);
            Category conduit = Category.GetCategory(_doc, BuiltInCategory.OST_Conduit);
            Category tray = Category.GetCategory(_doc, BuiltInCategory.OST_CableTray);
            double targetValue = 0.0;
            string targetValueStr = "";
            if (elem.Category.Id == pipe.Id)
            {
                targetValueStr = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE).AsString();
            }
            else if (elem.Category.Id == duct.Id)
            {
                targetValueStr = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE).AsString();
            }
            else if (elem.Category.Id == conduit.Id)
            {
                targetValueStr = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE).AsString();
            }
            else if (elem.Category.Id == tray.Id)
            {
                targetValueStr = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE).AsString();
            }
            return targetValueStr;
        }
        public Autodesk.Revit.DB.View createNewViewAndZoom(ExternalCommandData commandData)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uiapp.ActiveUIDocument.Document;
            ViewFamilyType vft3d = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(q => q.ViewFamily == ViewFamily.ThreeDimensional);
            Autodesk.Revit.DB.View view = null;
            using (Transaction t = new Transaction(doc, "make views"))
            {
                t.Start();
                view = View3D.CreateIsometric(doc, vft3d.Id);
                t.Commit();
            }
            zoom(uidoc, view);
            return view;
        }
        private void zoom(UIDocument uidoc, Autodesk.Revit.DB.View view)
        {
            // YOU NEED THESE TWO LINES OR THE ZOOM WILL NOT HAPPEN!
            uidoc.ActiveView = view;
            uidoc.RefreshActiveView();

            UIView uiview = uidoc.GetOpenUIViews().Cast<UIView>().FirstOrDefault(q => q.ViewId == view.Id);
            uiview.Zoom(5);
        }
        public void CropViewBySelection(Document doc, IList<Reference> pickPipeRefs, Autodesk.Revit.DB.View v)
        {
            //Autodesk.Revit.DB.View activeView = doc.ActiveView;
            List<XYZ> points = new List<XYZ>();
            foreach (Reference r in pickPipeRefs)
            {
                BoundingBoxXYZ bb = doc.GetElement(r).get_BoundingBox(v);
                points.Add(bb.Min);
                points.Add(bb.Max);
            }
            BoundingBoxXYZ sb = new BoundingBoxXYZ();
            sb.Min = new XYZ(points.Min(p => p.X),
                              points.Min(p => p.Y),
                              points.Min(p => p.Z));
            sb.Max = new XYZ(points.Max(p => p.X),
                           points.Max(p => p.Y),
                           points.Max(p => p.Z));
            try
            {
                using (Transaction t = new Transaction(doc, "Crop View By Selection"))
                {
                    t.Start();
                    v.CropBoxActive = true;
                    v.CropBox = sb;
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Revit", ex.Message);
                return;
            }
        }
    }
    public class PipeSelectionFilter : ISelectionFilter
    {
        private Document _doc;
        public PipeSelectionFilter(Document doc)
        {
            this._doc = doc;
        }
        public bool AllowElement(Element element)
        {
            Category pipe = Category.GetCategory(_doc, BuiltInCategory.OST_PipeCurves);
            Category duct = Category.GetCategory(_doc, BuiltInCategory.OST_DuctCurves);
            Category conduit = Category.GetCategory(_doc, BuiltInCategory.OST_Conduit);
            Category tray = Category.GetCategory(_doc, BuiltInCategory.OST_CableTray);
            //Category pipeFitting = Category.GetCategory(_doc, BuiltInCategory.OST_PipeFitting);
            if (element.Category.Id == pipe.Id)
            {
                return true;
            }
            else if (element.Category.Id == duct.Id)
            {
                return true;
            }
            else if (element.Category.Id == conduit.Id)
            {
                return true;
            }
            else if (element.Category.Id == tray.Id)
            {
                return true;
            }
            //else if (element.Category.Id == pipeFitting.Id)
            //{
            //    return true;
            //}
            return false;
        }

        public bool AllowReference(Reference refer, XYZ point)
        {
            return false;
        }
    }
    public class linkedPipeSelectionFilter : ISelectionFilter
    {
        private Document _doc;
        public linkedPipeSelectionFilter(Document doc)
        {
            this._doc = doc;
        }
        public bool AllowElement(Element element)
        {
            return true;
        }
        public bool AllowReference(Reference refer, XYZ point)
        {
            Category pipe = Category.GetCategory(_doc, BuiltInCategory.OST_PipeCurves);
            Category duct = Category.GetCategory(_doc, BuiltInCategory.OST_DuctCurves);
            Category conduit = Category.GetCategory(_doc, BuiltInCategory.OST_Conduit);
            Category tray = Category.GetCategory(_doc, BuiltInCategory.OST_CableTray);
            var elem = this._doc.GetElement(refer);
            if (elem != null && elem is RevitLinkInstance link)
            {
                var linkElem = link.GetLinkDocument().GetElement(refer.LinkedElementId);
                if (linkElem.Category.Id == pipe.Id)
                {
                    return true;
                }
                else if (linkElem.Category.Id == duct.Id)
                {
                    return true;
                }
                else if (linkElem.Category.Id == conduit.Id)
                {
                    return true;
                }
                else if (linkElem.Category.Id == tray.Id)
                {
                    return true;
                }
            }
            else
            {
                if (elem.Category.Id == pipe.Id)
                {
                    return true;
                }
                else if (elem.Category.Id == duct.Id)
                {
                    return true;
                }
                else if (elem.Category.Id == conduit.Id)
                {
                    return true;
                }
                else if (elem.Category.Id == tray.Id)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
