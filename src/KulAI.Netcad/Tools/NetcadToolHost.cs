using System;
using System.Collections.Generic;
using System.Linq;
using KulAI.Core.Llm;
using KulAI.Core.Tools;
using Netcad.Interop;

namespace KulAI.Netcad.Tools
{
    /// <summary>NETCAD araç seti. Çizimi değiştiren her araç tek bir geri alma bloğunda çalışır (tek Ctrl+Z).</summary>
    public sealed class NetcadToolHost : IToolHost
    {
        private readonly Dictionary<string, NetcadTool> _tools;

        public NetcadToolHost()
        {
            var all = new NetcadTool[]
            {
                new GetDrawingInfoTool(),
                new ListLayersTool(),
                new SetLayersVisibilityTool(),
                new CreateLayerTool(),
                new CreateLineTool(),
                new CreatePolylineTool(),
                new CreatePointTool(),
                new CreateTextTool(),
                new CreateCircleTool(),
                new ZoomExtentsTool(),
                new FindObjectsTool(),
                new SelectObjectsTool(),
                new DeleteObjectsTool(),
                new PreviewTableFileTool(),
                new ImportPointsFromFileTool(),
                new DivideParcelTool(),
                new CreateTableTool(),
                new FindCoordinateSystemTool(),
                new TransformCoordinatesTool(),
                new ProjectProjectionTool(),
                new CaptureViewTool(),
                new ReadPdfTool(),
                new ExportPdfTool(),
                new EditTextsTool(),
                new FindNetcadCommandTool(),
                new RunNetcadCommandTool(),
                new WriteEdgeLengthsTool(),
            };
            _tools = all.ToDictionary(t => t.Name);
            Specs = all.Select(t => t.ToSpec()).ToList();
        }

        public IReadOnlyList<ToolSpec> Specs { get; }

        public string GetConfirmation(ToolCall call, int confirmAboveObjectCount)
        {
            if (!_tools.TryGetValue(call.Name ?? "", out var tool)) return null;
            try { return tool.ConfirmationFor(call.Arguments, confirmAboveObjectCount); }
            catch (Exception ex) { MainModule.Log($"Onay hesaplanamadı ({call.Name}): {ex.Message}"); return null; }
        }

        public ToolResult Execute(ToolCall call)
        {
            var result = new ToolResult { ToolCallId = call.Id, ToolName = call.Name };
            if (!_tools.TryGetValue(call.Name ?? "", out var tool))
            {
                result.IsError = true;
                result.Content = "Bilinmeyen araç: " + call.Name;
                return result;
            }

            tool.Images.Clear();
            try
            {
                if (tool.ModifiesDrawing)
                {
                    using (NetcadApplication.BeginUndoBlock(true))
                        result.Content = tool.Execute(call.Arguments);
                    NetcadApplication.Current.FastRedraw();
                }
                else
                {
                    result.Content = tool.Execute(call.Arguments);
                }
            }
            catch (Exception ex)
            {
                result.IsError = true;
                result.Content = ex.Message;
                MainModule.Log($"Araç hatası ({call.Name}): {ex}");
            }
            result.Images.AddRange(tool.Images);
            tool.Images.Clear();
            return result;
        }
    }
}
