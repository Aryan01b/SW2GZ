# SolidWorks API Reference — SW2GZ Implementation

**Scope:** every SolidWorks COM API member SW2GZ's own codebase calls,
grouped by category, with exact file provenance, usage context, and a
plain-English explanation of what the member does. This is the *proven*
subset — every entry has at least one real call site in this repo as of
2026-07-01. Not a copy of the official docs; use this first, fall back to
the official reference (link below) for anything not covered here.

Sources:
- Local offline API browser (installed with SW):
  `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\apihelp.chm`
- Official API guide:
  https://help.solidworks.com/2025/english/api/sldworksapiprogguide/GettingStarted/SolidWorks_API_Getting_Started_Overview.htm
- Official object model reference:
  https://help.solidworks.com/2025/english/api/sldworksapi/Welcome.htm
  (swap the year in the URL to match the installed SW version)
- Interop assemblies referenced by `SW2GZ\SW2GZ.csproj`:
  `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\SolidWorks.Interop.{sldworks,swconst,swpublished,swcommands}.dll`
- Upstream lineage for inherited files: [`ros/solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter)

**Provenance flag used below:**
- **[active]** — called from SW2GZ's current, non-gutted code path.
- **[legacy]** — called only from inherited-upstream files
  (`ExportHelperExtension.cs`, `ConfigurationSerialization.cs`,
  `AssemblyExportForm.cs`, `ExportPropertyManager(Extension).cs`,
  `CommonSwOperations.cs`) that predate the robot-mode v2 gut
  (see `agent-progress/progress.md`). Still real, working COM calls — just
  not necessarily wired into the current export pipeline. Treat as a
  reference for *how it was done before*, not a guarantee it still runs.

---

## 1. App / Doc lifecycle

| API member | File(s) | Usage | What it does |
|---|---|---|---|
| `ISwAddin.ConnectToSW` | `SW\SwAddin.cs` **[active]** | entry point when SW loads the add-in; stores `SwApp`, wires `CmdMgr` + events | SW calls this to hand the add-in its `SldWorks` app object + connection cookie |
| `ISwAddin.DisconnectFromSW` | `SW\SwAddin.cs` **[active]** | tears down command group + event handlers, releases COM refs | SW calls this on add-in unload |
| `ISldWorks.SetAddinCallbackInfo` | `SW\SwAddin.cs` **[active]** | registers this add-in instance + cookie | tells SW which object to invoke for this add-in's UI callbacks |
| `ISldWorks.GetCommandManager` | `SW\SwAddin.cs` **[active]** | obtains `ICommandManager` | returns the ribbon/toolbar/menu builder object |
| `ISldWorks.ActiveDoc` | `SwAddin.cs`, `EventHandling.cs`, `ExportHelper.cs`, `ExportPropertyManager.cs`, `AssemblyExportForm.cs` **[active + legacy]** | read repeatedly | property returning the currently active open document |
| `ISldWorks.GetFirstDocument` / `IModelDoc2.GetNext` | `SW\SwAddin.cs` **[active]** | iterates open docs to wire per-doc event handlers | walks the open-document linked list |
| `IModelDoc2.GetType` | `SwAddin.cs`, `EventHandling.cs`, `ExportHelperExtension.cs`, `Sw2gzModelExporter.cs`, `Sw2gzModelPreviewer.cs`, `SolidWorksAssemblyWalker.cs`, `Sw2gzRibbonRegistrar.cs` **[active]** | branches Part/Assembly/Drawing handling and ribbon gating | returns integer document-type code (`swDocumentTypes_e`) |
| `IModelDoc2.GetTitle` | `SwAddin.cs`, `ExportHelper.cs`, `ExportHelperExtension.cs`, `ExportPropertyManager.cs`, `Sw2gzDocStore.cs`, `Sw2gzExportWizardForm.cs` **[active + legacy]** | default ROS package/link name source | returns document's window/title-bar name |
| `IModelDoc2.GetPathName` | `Sw2gzDocStore.cs`, `CommonSwOperations.cs`, `Sw2gzExportWizardForm.cs` **[active + legacy]** | keys the doc-config cache; logs component source path | returns full saved file-system path |
| `IModelDocExtension.ActiveCommandTab` (set) | `SW\SwAddin.cs` **[active]** | reactivates the "SW2GZ" ribbon tab after mode switches | gets/sets which CommandManager tab is currently shown |
| `IModelDoc2.GetFirstModelView` / `IModelView.GetNext` | `SW\EventHandling.cs` **[active]** | enumerates a doc's model views for event wiring | first/next graphics view of a document |
| `ISldWorks.SendMsgToUser2` | `SwAddin.cs`, all `UI\Pmp\*.cs` **[active]** | informational popups for gating failures / page-create failure | displays a SW-styled message box |
| `ISldWorks.GetUserProgressBar` | `URDFExport\ExportHelper.cs` **[active]** | progress-bar handle for long exports | returns SW's built-in progress-bar UI object |
| `ISldWorks.GetMathUtility` | `URDFExport\ExportHelper.cs` **[active]** | cached once as `swMath`, used for matrix ops | returns SW's vector/matrix/transform math utility |
| `Get/SetUserPreferenceToggle` | `URDFExport\ExportHelper.cs` **[active]** | saves/restores STL export toggles (binary/preview/positive-translate/one-file) around export | gets/sets a boolean SW app preference |
| `Get/SetUserPreferenceIntegerValue` | `URDFExport\ExportHelper.cs` **[active]** | saves/restores STL units + quality constants | gets/sets an integer SW app preference |
| `Get/SetUserPreferenceDoubleValue` | `URDFExport\ExportHelper.cs` **[active]** | saves/restores component-hide/view-transition speed | gets/sets a double SW app preference |
| `ISldWorks.DefineAttribute` | `URDFExport\Sw2gzConfigSerialization.cs`, `URDFExport\Sw2gzDocSerialization.cs`, `AssemblyExportForm.cs`, `ConfigurationSerialization.cs` **[active + legacy]** | defines a custom `AttributeDef` type used to persist SW2GZ's whole doc-model on the SLDASM file | begins defining a new custom document-attribute type |
| `AttributeDef.AddParameter` | same files **[active + legacy]** | adds `data`(string)/`date`(string)/`version`(double) fields | adds a named/typed field to an attribute definition |
| `AttributeDef.Register` | same files **[active + legacy]** | finalizes definition before instancing | registers the attribute type with SolidWorks |
| `AttributeDef.CreateInstance5` | same files **[active + legacy]** | `(model, null template, name, options, swAllConfiguration)` | creates an attribute-feature instance on a model doc |
| `SolidWorks.Interop.sldworks.Attribute.GetName` | same files **[active + legacy]** | compared against target attribute name while scanning features | reads the attribute feature's registered name |
| `Attribute.GetParameter` | same files **[active + legacy]** | fetches `data`/`date`/`version` `Parameter` objects | opens one field of the hidden data tag |
| `Parameter.GetStringValue` / `.GetDoubleValue` | same files **[active + legacy]** | reads serialized XML payload / numeric version | reads a string/double attribute parameter value |
| `Parameter.SetStringValue2` / `.SetDoubleValue2` | same files **[active + legacy]** | writes with `swAllConfiguration` scope | writes a string/double attribute parameter value |
| `Feature.Select2` | `Sw2gzDocSerialization.cs` **[active]** | selects the attribute feature, append flag + mark | selects the feature in the model so it can be deleted |
| `ModelDoc2.EditDelete` | `Sw2gzDocSerialization.cs` **[active]** | deletes current selection | deletes the selected feature (removes an old attribute tag) |
| `IModelDoc2.ConfigurationManager` → `.ActiveConfiguration` | `ExportHelperExtension.cs` **[legacy]** | reads active `Configuration` before applying "URDF Export" display state | exposes the doc's current active configuration |
| `Configuration.GetDisplayStates` / `.ApplyDisplayState` | `ExportHelperExtension.cs` **[legacy]** | switches to a dedicated display state before geometry walk | lists/activates a configuration's named display states |
| `IModelDoc2.MaterialPropertyValues` | see §7 Color/appearance | | |
| `IModelDoc2.ClearSelection2` / `GraphicsRedraw2` | multiple, see §5/§8 | | |

## 2. Ribbon / CommandManager

All **[active]**, all in `UI\Ribbon\Sw2gzRibbonRegistrar.cs` + `SW\SwAddin.cs`.

| API member | Usage | What it does |
|---|---|---|
| `ICommandManager.GetGroupDataFromRegistry` | probes cached registry IDs before forcing a fresh group | checks if SW has stale cached command-group data |
| `ICommandManager.CreateCommandGroup2` | builds the "SW2GZ" command group, `ignorePrevious=true` | creates a new set of ribbon/toolbar commands |
| `ICommandManager.RemoveCommandGroup` | `SwAddin.cs`, on disconnect | tears down a previously-registered command group |
| `ICommandManager.GetCommandTab` | resolves live tab handle for assembly + part doc types by title | gets an existing ribbon tab for a document type |
| `ICommandManager.AddCommandTab` | called for both assembly and part doc types | creates a new ribbon command tab |
| `ICommandManager.RemoveCommandTab` | drops existing tab before full rebuild | removes a ribbon command tab |
| `ICommandGroup.IconList` | set to sprite-strip PNG paths | per-button glyph images |
| `ICommandGroup.MainIconList` | set to single-glyph cube icon set | the command-group's own toolbar glyph |
| `ICommandGroup.AddCommandItem2` | called 18× with name/tip/callback/enable/image/userId | registers one clickable command button |
| `ICommandGroup.HasToolbar` / `.HasMenu` | `HasToolbar=true`, `HasMenu=false` | toolbar vs menu display mode |
| `ICommandGroup.Activate` | must run before `get_CommandID` resolves valid IDs | finalizes/registers the command group |
| `ICommandGroup.get_CommandID` | called post-`Activate` for every `AddCommandItem2` index | resolves a button's internal command ID |
| `CommandTab.AddCommandTabBox` | per cluster (mode-start/actions/mode-cluster/part boxes) | adds a grouped-button box to a ribbon tab |
| `CommandTab.RemoveCommandTabBox` | wrapped in try/catch during `RefreshTabForMode` swap | removes a button box from a ribbon tab |
| `ICommandTabBox.AddCommands` | takes parallel `cmdId[]` + `textType[]` arrays | populates a ribbon box with specific buttons + label styles |

## 3. PropertyManagerPage (native wizard UI) — **[active]**

Files: `UI\Pmp\Sw2gzCreateWorldPmp.cs`, `Sw2gzCreateAssetPmp.cs`,
`Sw2gzWorldSensorsPmp.cs`, `Sw2gzWorldSettingsPmp.cs`, `Sw2gzStubPmp.cs`,
plus legacy-pipeline `URDFExport\ExportPropertyManager.cs` /
`GeometryPropertyManager.cs` **[legacy]**.

### 3.1 Page scaffolding
| API member | Usage | What it does |
|---|---|---|
| `SldWorks.CreatePropertyManagerPage` | every PMP class | creates a `PropertyManagerPage2` bound to an `IPropertyManagerPage2Handler9` COM callback object |
| `PropertyManagerPage2.AddGroupBox` | builds each step/section group | adds a collapsible titled group box |
| `PropertyManagerPage2.Show2` | every wizard's `Show()` wrapper, called with `0` | displays the page in SW's left dock |
| `PropertyManagerPage2.Close` | `GoNext` on final step / OK-Cancel | programmatically closes the PMP as if OK/Cancel pressed |
| `PropertyManagerPage2.SetCursor` | `ExportPropertyManager.cs` **[legacy]** | advances focus after RMB pick (`swPropertyManagerPageCursors_Advance`) |
| `PropertyManagerPage2.SetFocus` | `ExportPropertyManager.cs` **[legacy]** | focuses the embedded `WindowFromHandle` tree control |

### 3.2 Controls
| Control type | Key members | What it does |
|---|---|---|
| `PropertyManagerPageGroup` | `.AddControl2(id, type, caption, align, options, tip)`, `.Visible` | adds a widget into a group / toggles a wizard "page" |
| `PropertyManagerPageSelectionbox` | `.SingleEntityOnly`, `.Height`, `.Mark`, `.SetSelectionFilters`, `.AllowMultipleSelectOfSameEntity`, `.AllowSelectInMultipleBoxes`, `.SetSelectionFocus` | native viewport-pick control; `Mark` tags it so later `GetSelectedObjectCount2/6` calls can target this specific box |
| `PropertyManagerPageListbox` | `.Height`, `.Clear`, `.AddItems`, `.CurrentSelection` | multi-row string list (used for World-mode asset list) |
| `PropertyManagerPageCombobox` | `.AddItems`, `.CurrentSelection`, `.Style` (`swPropMgrPageComboBoxStyle_EditBoxReadOnly`), `.Height`, `.EditText`, `.get_ItemText`, `.Clear` | dropdown; `Style` forces read-only selection-only mode |
| `PropertyManagerPageNumberbox` | `.SetRange2(unitType, min, max, resolution, ...)`, `.Value` | numeric spinner with range/precision/increment |
| `PropertyManagerPageCheckbox` | `.Checked` | boolean toggle (static/friction/compute-inertia in SW2GZ) |
| `PropertyManagerPageTextbox` | `.Text` | free-text field |
| `PropertyManagerPageLabel` | `.Caption` | text label, updated live for status/step-description text |
| `PropertyManagerPageWindowFromHandle` | `.SetWindowHandlex64(hwnd)`, `.Height` | embeds an arbitrary WinForms panel/TreeView HWND inside the native page — **this is how SW2GZ's actual wizard chrome (Back/Next nav bar, dark theme) works**, since native PMP buttons crash SW (see 3.4) |
| `IPropertyManagerPageControl` (base, cast target) | `.Enabled`, `.Visible`, `.Width`, `.Tip` | generic control properties any typed control also exposes |

### 3.3 `IPropertyManagerPage2Handler9` callback interface

The mandatory COM callback contract every PMP class implements. In SW2GZ,
almost all ~35 members are **intentional no-op stubs** — only a handful
carry real logic:

| Member | SW2GZ behavior |
|---|---|
| `AfterActivation` | fires once page is active; World/Asset PMPs call `ShowStep()` here |
| `OnClose` | reads `Reason` (`swPropertyManagerPageClose_Okay`/`_Cancel`) to decide commit-vs-`Sw2gzDocSnapshot.Restore` rollback |
| `AfterClose` | invokes the `onCommit`/`onClosed` continuation callback once, with the live doc if OK |
| `OnButtonPress` | **intentionally no-op** — native PMP buttons corrupt SW's renderer (burned-in gotcha, see §3.4); all button logic lives in the WinForms nav bar instead |
| `OnComboboxSelectionChanged` | no-op in most PMPs; real logic only in `GeometryPropertyManager.cs` **[legacy]** (drives `GoToLink`) |
| `OnCheckboxCheck` | no-op — native checkboxes AV-crash SW (see §3.4); state read on `Close`/commit instead |
| `OnSelectionboxListChanged` | wires `UpdateSelCount` / cursor-advance in the legacy pipeline |
| `OnSubmitSelection` | returns `true` (accept all); `GeometryPropertyManager.cs` **[legacy]** validates entity type before accepting |
| `OnTextboxChanged` | no-op in current PMPs; legacy pipeline live-syncs link name into tree node |
| `OnNumberboxChanged` | no-op in current PMPs; legacy pipeline triggers `CreateNewNodes` on child-count change |
| All others (`OnGainedFocus/OnLostFocus/OnHelp/OnNextPage/OnPreviousPage/OnPreview/OnTabClicked/OnKeystroke/OnSelectionboxFocusChanged/OnSelectionboxCalloutCreated/Destroyed/OnNumberBoxTrackingCompleted/OnComboboxEditChanged/OnListboxSelectionChanged/OnListboxRMBUp/OnGroupCheck/OnGroupExpand/OnOptionCheck/OnPopupMenuItem/OnPopupMenuItemUpdate/OnSliderPositionChanged/OnSliderTrackingCompleted/OnRedo/OnUndo/OnWhatsNew/OnWindowFromHandleControlCreated/OnActiveXControlCreated`) | mandatory interface stubs, unused — required because the interface must be fully implemented even for events SW2GZ never needs |

### 3.4 Gotchas burned in (don't relitigate these)

- **Native PMP buttons (`swControlType_Button`) corrupt SW's PMP
  renderer** when the click handler mutates PMP state — buttons vanish,
  multi-select glitches, theme breaks. Fix used everywhere: host the
  wizard's actual Back/Next/action buttons in a WinForms panel embedded
  via `PropertyManagerPageWindowFromHandle.SetWindowHandlex64`, deferred
  with `BeginInvoke` to escape click-handler re-entrancy.
- **Native PMP checkboxes AV-crash SW** on toggle even with an empty
  `OnCheckboxCheck` handler — same WinForms-embedding fix.
- **`internal` ComVisible classes are NOT exposed via CCW.** All PMP
  classes must be `public sealed` or `CreatePropertyManagerPage`'s handler
  param silently throws `InvalidCastException`.
- **`AddGroupBox` needs `swGroupBoxOptions_Visible | swGroupBoxOptions_Expanded`** — passing `0` renders an empty collapsed shell.

## 4. Assembly / Component structure

| API member | File(s) | Usage | What it does |
|---|---|---|---|
| `AssemblyDoc.GetComponents` | `SolidWorksAssemblyWalker.cs`, `SolidWorksMeshTessellator.cs`, `SolidWorksMassProperties.cs`, `Sw2gzCreateWorldPmp.cs`, `SwJointStateSampler.cs`, `CommonSwOperations.cs` **[active + legacy]** | root traversal (top-level or all, via `bool` arg) | lists the assembly's component instances |
| `Component2.GetChildren` | walker, tessellator, mass-props **[active]** | recursion into sub-assembly contents | child `Component2[]` of a component |
| `Component2.Name2` | everywhere **[active + legacy]** | sanitized link/leaf identifier, dedup key | instance-unique component name |
| `Component2.GetParent` | walker, `ExportHelperExtension.cs` **[active + legacy]** | walks up to find top-level owning component | immediate parent `Component2`, null at top level |
| `Component2.GetModelDoc2` / `.GetModelDoc` | tessellator, walker, `EventHandling.cs`, `ExportHelper.cs` **[active + legacy]** | checks doc type / opens underlying doc | `IModelDoc2` referenced by a component |
| `Component2.GetBodies2` / `.GetBodies3` | `SolidWorksMeshTessellator.cs` **[active]**, `ExportHelperExtension.cs` **[legacy]** | filtered by `swSolidBody` | solid bodies owned by the component |
| `Component2.IsSuppressed` | tessellator, `Sw2gzCreateWorldPmp.cs` **[active]** | filters suppressed comps out of auto-seeded asset list / mesh export | whether component is currently suppressed |
| `Component2.IsHidden` | `CommonSwOperations.cs` **[legacy]** | builds hidden-component exclusion list before STL export | whether component is currently hidden |
| `Component2.IsFixed` | `ExportHelperExtension.cs` **[legacy]** | DOF-probe prep | whether component is rigidly fixed |
| `Component2.Transform2` → `MathTransform.ArrayData` | tessellator, joint sampler, walker, `MathOPS.cs` **[active]**; `ExportHelperExtension.cs` **[legacy]** | **the central geometric primitive** — maps part-local mesh/mate/axis data into assembly frame | component's placement transform (3×3 rotation + translation + scale, 16 doubles row-major) |
| `Component2.GetMaterialPropertyValues2` | `SolidWorksMeshTessellator.cs` **[active]** | instance color override, preferred over part material | `double[9]`: R,G,B,Ambient,Diffuse,Specular,Shininess,Transparency,Emission |
| `Component2.GetMates` | `ExportHelperExtension.cs` **[legacy]** | finds/suppresses limit mates before DOF probing | mate features referencing this component |
| `Component2.GetID` | `ExportHelperExtension.cs` **[legacy]** | logging during fix/unfix toggling | numeric ID uniquely identifying the component instance |
| `Component2.Select4` | walker, `CommonSwOperations.cs`, `AssemblyExportForm.cs`, `ExportPropertyManagerExtension.cs` **[active + legacy]** | highlights a link's owning component in viewport | adds/replaces component in current selection |
| `Component2.GetBox` | `ExportHelperExtension.cs` **[legacy]** | clamps auto-generated joint origin inside bounding box | axis-aligned bounding-box corners |
| `AssemblyDoc.ResolveAllLightWeightComponents` | `ExportPropertyManager.cs` **[legacy]** | pre-export prep, `ResolveAllLightWeightComponents(true)` | forces all lightweight components to fully resolve/load |
| `AssemblyDoc.FixComponent` / `.UnfixComponent` | `ExportHelperExtension.cs` **[legacy]** | temporarily fixes parent chain to isolate one component's free DOF | locks/unlocks a component's degrees of freedom |
| `Component2.GetRemainingDOFs` *(undocumented API)* | `ExportHelperExtension.cs` **[legacy]** | many out-params (`R1Status`, `RPoint1`, `RDir1`, ...) — auto-detects joint type/axis/origin from unconstrained DOF | computes the unconstrained degrees of freedom of a component |
| `IPartDoc.GetBodies2` | `SolidWorksMeshTessellator.cs` **[active]** | solid bodies of a standalone part doc | bodies contained directly in a part (Asset-mode whole-part export) |

## 5. Mates

All `SolidWorksAssemblyWalker.cs` **[active]** unless noted.

| API member | Usage | What it does |
|---|---|---|
| `IModelDoc2.FirstFeature` | entry point for feature-tree walk | first `Feature` in the model tree |
| `Feature.GetNextFeature` | iterates top-level feature tree | next sibling feature |
| `Feature.GetFirstSubFeature` / `.GetNextSubFeature` | iterates mate features nested under the `MateGroup` folder | first/next child feature under a parent |
| `Feature.GetTypeName2` | identifies `"MateGroup"` | internal type-name string of a feature |
| `Feature.GetSpecificFeature2` | casts generic `Feature` → `Mate2` | the type-specific object wrapped by a feature |
| `Feature.Name` | compared against user-picked mate name | feature's display name |
| `Mate2.Type` | primary signal for mate→joint-kind classification (`swMateType_e`: CONCENTRIC, COINCIDENT, DISTANCE, ANGLE, SLOT, LOCK) | the mate's type constant |
| `Mate2.MaximumVariation` / `.MinimumVariation` | detects limit mates → derives joint limits | upper/lower travel range of a limit mate |
| `Mate2.Flipped` | `ExportHelperExtension.cs` **[legacy]**, sign convention for joint limits | whether mate alignment is reversed |
| `Mate2.GetMateEntityCount` | iterates coupled geometric references | count of `MateEntity2` in a mate |
| `Mate2.MateEntity(i)` | fetches each entity | `MateEntity2` at index `i` |
| `MateEntity2.ReferenceComponent` | identifies owning component for parent/child link resolution | the `Component2` owning a mate entity's reference |
| `MateEntity2.Reference` | actual selectable geometry behind a mate entity | `Face2`/`Edge`/`Entity` referenced |
| `MateEntity2.EntityParams` | generic origin+direction fallback when typed extraction fails | `double[6]`: origin + direction |
| `Face2.GetSurface` | classifies mate reference faces plane vs cylinder | underlying `Surface` geometry object |
| `Surface.IsPlane` / `.IsCylinder` | face classification | boolean type check |
| `Surface.PlaneParams` | `double[6]`: normal + point | flat face's orientation |
| `Surface.CylinderParams` | `double[7]`: origin + axis-direction + radius | cylinder's centerline location + direction — the primary axis-extraction path for concentric (revolute/continuous) joints |
| `IEdge.GetCurveParams2` | `ExportHelperExtension.cs` **[legacy]**, fallback path | edge-midpoint mate reference when face extraction fails |
| `Entity.Select4` | highlights the mate's reference geometry on screen | selects a generic `Entity` in the viewport |
| `IModelDoc2.GraphicsRedraw2` | forces viewport redraw after highlighting mate geometry | redraws the 3D graphics view |
| `Feature.Select` (Mate2-as-Feature) | `ExportHelperExtension.cs` **[legacy]** | selects a mate/feature in the tree |
| `Feature.SetSuppression2` (Mate2-as-Feature) | `ExportHelperExtension.cs` **[legacy]**, around DOF probing | suppresses/unsuppresses a mate/feature |

## 6. Geometry / tessellation

All `SolidWorksMeshTessellator.cs` **[active]**.

| API member | Usage | What it does |
|---|---|---|
| `Body2.GetTessellation(null)` | requests tessellation of all faces of a solid body | creates an `ITessellation` object for the body |
| `ITessellation.NeedVertexNormal` / `.NeedFaceFacetMap` / `.NeedEdgeFinMap` | all set `false` to reduce overhead | flags controlling optional tessellation data generation |
| `ITessellation.Tessellate()` | triggers computation, returns `bool` success | performs the triangulation algorithm |
| `ITessellation.GetFacetCount()` | drives the triangle-emission loop | number of triangular facets produced |
| `ITessellation.GetFacetFins(f)` | per-facet lookup | `int[3]` fin (edge) IDs for facet `f` |
| `ITessellation.GetFinVertices(fin)` | per-fin lookup | `int[2]` vertex IDs for a fin |
| `ITessellation.GetVertexPoint(v)` | part-local coords, baked into assembly frame via `Component2.Transform2` | `double[3]` XYZ of vertex `v` |

## 7. Mass properties

`SolidWorksMassProperties.cs` **[active]**, `ExportHelperExtension.cs` **[legacy]**.

| API member | Usage | What it does |
|---|---|---|
| `ModelDoc2.Extension` → `ModelDocExtension.CreateMassProperty` | creates an `IMassProperty` calculator scoped to current selection/config | sets up mass-properties calculation |
| `IMassProperty.Mass` | checked `≤0` to detect missing material | total mass in kg |
| `IMassProperty.CenterOfMass` | `double[3]` | XYZ center-of-mass coordinates, baked into `Link.Inertial.Origin` |
| `IMassProperty.GetMomentOfInertia(swMassPropertyMomentAboutCenterOfMass)` | `double[9]` | inertia tensor about a reference frame |
| `MassProperty.SetCoordinateSystem(MathTransform)` | `ExportHelperExtension.cs` **[legacy]**, scopes calc to a joint's frame | sets the reference coordinate system for mass calculations |
| `MassProperty.AddBodies(Body2[])` | `ExportHelperExtension.cs` **[legacy]**, restricts calc to per-link body subset | adds specific solid bodies to the mass-property calculation set |

## 8. Coordinate systems / reference geometry

| API member | File(s) | Usage | What it does |
|---|---|---|---|
| `IModelDocExtension.GetCoordinateSystemTransformByName` | `ExportHelper.cs`, `ExportHelperExtension.cs` **[active + legacy]** | primary read path — resolves a link's joint-origin coordinate system by name | `MathTransform` of a named coordsys feature |
| `RefAxis.GetRefAxisParams` | `ExportHelperExtension.cs` **[legacy]** | `{startX,Y,Z,endX,Y,Z}` | start/end point coordinates defining a reference axis |
| `FeatureManager.GetFeatures` filtered by `GetTypeName2() == "CoordSys"/"RefAxis"` | `ExportHelperExtension.cs` **[legacy]** | discovery, feature-tree search incl. sub-components | enumerates named ref-geometry features |
| `IMathTransform.ArrayData` | tessellator, walker, joint sampler, `CylinderTransform.cs`, `ExportHelperExtension.cs`, `MathOPS.cs` **[active + legacy]** | **the shared numeric primitive threading through the whole export pipeline** | 16-double row-major rotation+translation+scale+padding |
| `IMathTransform.Multiply` | `ExportHelperExtension.cs` **[legacy]** | composes coordsys-local transform with component's `Transform2` | multiplies (composes) two transforms |
| `IMathPoint.ArrayData` / `IMathVector.ArrayData` | `ExportHelperExtension.cs` **[legacy]** | reads out-params from `GetRemainingDOFs` directly | coordinate/vector components |
| `IFeatureManager.InsertCoordinateSystem(false,false,false)` | `ExportHelperExtension.cs` **[legacy]**, authoring | creates a coordinate-system feature from 3 selected sketch points | inserts a new `Feature` |
| `IModelDoc2.InsertAxis2(true)` | `ExportHelperExtension.cs` **[legacy]**, authoring | creates a reference axis from a selected sketch line | inserts a reference-axis feature |
| `IModelDoc2.SketchManager` → `.Insert3DSketch(true)` / `.ActiveSketch` | `ExportHelperExtension.cs` **[legacy]** | opens/closes a 3D sketch for editing ("URDF Reference" construction geometry) | accesses 2D/3D sketch creation API |
| `SketchManager.CreatePoint(x,y,z)` | `ExportHelperExtension.cs` **[legacy]** | returns `SketchPoint` | creates a 3D sketch point |
| `SketchManager.CreateLine(x1,y1,z1,x2,y2,z2)` | `ExportHelperExtension.cs` **[legacy]** | returns `SketchSegment` | creates a line segment in the active sketch |
| `SketchSegment.ConstructionGeometry` (set `true`) | `ExportHelperExtension.cs` **[legacy]** | marks a sketch line non-solid | construction geometry flag |
| `SketchSegment.Width` | `ExportHelperExtension.cs` **[legacy]** | set to `2` | display line width |
| `SketchSegment.Select4` / `SketchPoint.Select4` | `ExportHelperExtension.cs` **[legacy]** | selects sketch geometry via `SelectData.Mark` | selects using selection-mark data |
| `IModelDocExtension.SelectByID2` | `ExportHelperExtension.cs`, `ExportPropertyManagerExtension.cs` **[legacy]** | selects named `COORDSYS`/`AXIS`/`SKETCH`/`ATTRIBUTE`/`EXTSKETCHPOINT` entities before authoring ops | selects a named entity by type string |
| `FeatureManager.InsertFeatureTreeFolder2` / `.MoveToFolder` | `ExportPropertyManagerExtension.cs` **[legacy]** | organizes newly-created ref-geometry into a folder | creates/moves features into a named feature-tree folder |
| `Feature.Name` (set) | `ExportHelperExtension.cs` **[legacy]** | names newly created coordsys/axis features | sets a feature's display name |

## 9. Selection

| API member | File(s) | Usage | What it does |
|---|---|---|---|
| `IModelDoc2.SelectionManager` | `SwViewportSelectionService.cs`, `SolidWorksAssemblyWalker.cs`, `AssemblyExportForm.cs`, `CommonSwOperations.cs`, `GeometryPropertyManager.cs`, `Sw2gzCreateWorldPmp.cs`, `Sw2gzCreateAssetPmp.cs` **[active + legacy]** | gateway | returns `ISelectionMgr` |
| `ISelectionMgr.GetSelectedObjectCount2(mark)` | same set | counts current selection filtered by mark (`-1` = all) | how many items are selected under a group |
| `ISelectionMgr.GetSelectedObject6(index, mark)` | same set | retrieves the actual selected entity | raw COM object at index/mark |
| `ISelectionMgr.GetSelectedObjectType3(index, mark)` | `SwViewportSelectionService.cs`, `GeometryPropertyManager.cs` **[active + legacy]** | distinguishes body/surface/component selection types | `swSelectType_e` of the object |
| `ISelectionMgr.CreateSelectData()` | `AssemblyExportForm.cs`, `CommonSwOperations.cs`, `ExportHelperExtension.cs` **[legacy]** | builds `SelectData` for tagging selections | creates a selection-data options object |
| `SelectData.Mark` (set) | same **[legacy]** | tags a selection batch for later filtered read-back | integer "selection group" tag |
| `IModelDoc2.ClearSelection2(bool)` | everywhere **[active + legacy]** | reset before re-highlighting or fresh pick | clears current viewport selection |
| `IModelDocExtension.SelectByID2` | see §8 | | |
| `Entity.Select4(append, selectData)` | walker, `CommonSwOperations.cs`, `AssemblyExportForm.cs`, `ExportHelperExtension.cs` **[active + legacy]** | the viewport-highlight primitive used throughout | selects/appends an entity in the viewport |

## 10. Events

All `SW\EventHandling.cs` **[active]**, wired from `SW\SwAddin.cs`.

| API member | SW2GZ handler | What it does |
|---|---|---|
| `DSldWorksEvents.ActiveDocChangeNotify` | `OnDocChange` → `SyncRibbonToActiveDoc` | fires when the active document switches |
| `DSldWorksEvents.DocumentLoadNotify2` | `OnDocLoad` (no-op) | fires while a document is being loaded |
| `DSldWorksEvents.FileNewNotify2` | `OnFileNew`, re-walks open docs to attach handlers | fires when a new document is created |
| `DSldWorksEvents.ActiveModelDocChangeNotify` | `OnModelChange` (no-op) | fires when the active model document changes |
| `DSldWorksEvents.FileOpenPostNotify` | re-attaches doc events + syncs ribbon | fires after a file finishes opening |
| `DPartDocEvents.DestroyNotify` | `OnDestroy` in `PartEventHandler`, detaches all handlers | fires when a part document is closed |
| `DPartDocEvents.NewSelectionNotify` | static `OnNewSelection` (no-op) | fires when part-doc selection changes |
| `DAssemblyDocEvents.DestroyNotify` | `OnDestroy` in `AssemblyEventHandler` | fires when an assembly is closed |
| `DAssemblyDocEvents.NewSelectionNotify` | static `OnNewSelection` (no-op) | fires when assembly selection changes |
| `DAssemblyDocEvents.ComponentStateChangeNotify2` | gives old+new suppression state, routes to attach handlers when a component resolves | fires when a component's resolved/suppressed state changes |
| `DAssemblyDocEvents.ComponentStateChangeNotify` | legacy variant, bound alongside v2 | older component-state-change notification |
| `DAssemblyDocEvents.ComponentVisualPropertiesChangeNotify` | resolves component's ModelDoc2, treated as state change | fires when a component's visual/display properties change |
| `DAssemblyDocEvents.ComponentDisplayStateChangeNotify` | treated as state change | fires when a component's display state changes |
| `DDrawingDocEvents.DestroyNotify` | detaches handlers for closing drawing | fires when a drawing is closed |
| `DDrawingDocEvents.NewSelectionNotify` | no-op | fires when drawing selection changes |
| `DModelViewEvents.DestroyNotify2` | static `OnDestroy` in `DocView` (no-op) | fires when a graphics view is destroyed |
| `DModelViewEvents.RepaintNotify` | static `OnRepaint` (no-op) | fires when a graphics view repaints |

## 11. Color / appearance

| API member | File(s) | Usage | What it does |
|---|---|---|---|
| `Component2.GetMaterialPropertyValues2(swThisConfiguration, null)` | `SolidWorksMeshTessellator.cs` **[active]** | instance-level appearance override, preferred over part material | `double[9]` per-component color/material override |
| `IModelDoc2.MaterialPropertyValues` | `SolidWorksMeshTessellator.cs` **[active]**, `ExportHelperExtension.cs` **[legacy]** | fallback when no instance override exists | `double[9]` part-doc base material array |

Both arrays: `[R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission]`.

## 12. Persistence / utility

| API member | File(s) | Usage | What it does |
|---|---|---|---|
| `ModelDocExtension.GetPersistReference3` | `CommonSwOperations.cs` **[legacy]** | converts a `Component2` reference into a durable PID | persistent byte-array ID for a model object |
| `ModelDocExtension.GetObjectByPersistReference3` | `CommonSwOperations.cs` **[legacy]** | resolves a saved PID back into a live `Component2` | round-trips a persistent-reference ID back to a live COM object |
| `swPersistReferencedObjectStates_e` | `CommonSwOperations.cs` **[legacy]** | branches logging when a saved PID fails to resolve | enumerates why a persistent-reference lookup failed |
| `IModelDoc2.ShowComponent2` / `.HideComponent2` | `CommonSwOperations.cs` **[legacy]** | acts on current selection around per-link mesh export | shows/hides currently-selected components |
| `swComponentSuppressionState_e` | `SW\EventHandling.cs` **[active]** | interprets `ComponentStateChangeNotify` event payload | enumerates resolved/suppressed/lightweight states |
| `Marshal.ReleaseComObject` *(.NET interop, not SW API)* | `SolidWorksMeshTessellator.cs`, `SolidWorksMassProperties.cs`, `SolidWorksAssemblyWalker.cs` **[active]** | manually frees a COM object, always in `finally` | avoids leaking SolidWorks COM handles — copy this pattern for any new COM-touching code |

---

## Confirmed NOT used (checked, zero hits)

Grepped for and came back empty — SW2GZ's problem domain never needed these:

- `ISldWorks.OpenDoc6` / `CloseDoc` — SW2GZ only ever reads `ActiveDoc`, never opens/closes docs programmatically
- `ISldWorks.EnableSelection`
- `IRenderMaterial` / `GetRenderMaterial` / `SetMaterialPropertyValues` — modern PBR appearance API; SW2GZ uses the legacy `double[9]` property instead
- `ICoordinateSystemFeatureData` — structured coordsys feature edit; SW2GZ reads via `GetCoordinateSystemTransformByName` only

## Not touched at all (whole subsystems)

Drawings/views, sheet metal, weldments, configuration authoring beyond
`ActiveConfiguration`/display-state read, custom properties, equations,
Toolbox, Routing, Costing, Simulation/Motion Study API, PDM. The
corresponding interop DLLs (`SolidWorks.Interop.SWRoutingLib`,
`.sldcostingapi`, `.sldtoolboxconfigureaddin`, `.swmotionstudy`,
`.sustainability`, etc.) sit in
`C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\` unused — not
referenced by `SW2GZ.csproj`.
