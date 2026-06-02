/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — IViewportSelectionService backed by the SolidWorks ISelectionMgr.
Returns the names of bodies/components currently selected in the viewport so
the Links step can assign geometry. Follows the SolidWorksMassProperties
pattern: parameterless skeleton ctor (returns empty) + real ctor taking the
SW handle, with the COM body guarded by #if SW_INTEROP.

Compiled only into SW2GZ.csproj (net48); NOT source-linked into the test
project. The Links VM is tested against the pure FakeViewportSelectionService.
*/
using System;
using System.Collections.Generic;
using SW2GZ.UI.Services;

#if SW_INTEROP
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace SW2GZ.UI.Services.Sw
{
    public sealed class SwViewportSelectionService : IViewportSelectionService
    {
#if SW_INTEROP
        private readonly SldWorks _swApp;

        public SwViewportSelectionService(SldWorks swApp)
        {
            _swApp = swApp;
        }
#endif

        // Skeleton ctor — yields an empty selection when no SW handle is present.
        public SwViewportSelectionService() { }

        public IReadOnlyList<string> GetSelectedBodyNames()
        {
#if SW_INTEROP
            if (_swApp == null)
                return Array.Empty<string>();

            IModelDoc2 model = _swApp.IActiveDoc2;
            if (model == null)
                return Array.Empty<string>();

            ISelectionMgr selMgr = (ISelectionMgr)model.SelectionManager;
            if (selMgr == null)
                return Array.Empty<string>();

            int count = selMgr.GetSelectedObjectCount2(-1);
            var names = new List<string>(count);
            for (int i = 1; i <= count; i++)
            {
                object selObj = selMgr.GetSelectedObject6(i, -1);
                string name = DescribeSelection(selObj, selMgr.GetSelectedObjectType3(i, -1));
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            return names;
#else
            return Array.Empty<string>();
#endif
        }

        public int SelectedCount => GetSelectedBodyNames().Count;

#if SW_INTEROP
        // Best-effort name resolution: bodies expose Name; components expose
        // Name2. Other selection types fall back to their type string so the
        // user still sees a non-empty selection count.
        private static string DescribeSelection(object selObj, int selType)
        {
            switch ((swSelectType_e)selType)
            {
                case swSelectType_e.swSelSOLIDBODIES:
                case swSelectType_e.swSelSURFACEBODIES:
                    return selObj is Body2 body ? body.Name : null;
                case swSelectType_e.swSelCOMPONENTS:
                    return selObj is Component2 comp ? comp.Name2 : null;
                default:
                    return ((swSelectType_e)selType).ToString();
            }
        }
#endif
    }
}
