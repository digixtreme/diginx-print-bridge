import http from "node:http";

const HOST = process.env.DIGINX_PRINT_BRIDGE_HOST || "127.0.0.1";
const PORT = Number(process.env.DIGINX_PRINT_BRIDGE_PORT || "17777");
const allowedOrigins = new Set((process.env.DIGINX_PRINT_BRIDGE_ALLOWED_ORIGINS || "").split(",").map((v) => v.trim()).filter(Boolean));
const protocol = "1";
const printers = [
  { printerId: "receipt-printer-01", label: "Receipt Printer 01", state: "ready" },
  { printerId: "receipt-printer-02", label: "Fallback Printer 02", state: "ready" },
];
const recentJobs = [];

function originAllowed(origin) {
  if (!origin) return true;
  if (allowedOrigins.has(origin)) return true;
  try { const u = new URL(origin); return (u.hostname === "localhost" || u.hostname === "127.0.0.1" || u.hostname === "[::1]") && (u.protocol === "http:" || u.protocol === "https:"); }
  catch { return false; }
}
function cors(res, origin) {
  if (origin && originAllowed(origin)) res.setHeader("access-control-allow-origin", origin);
  res.setHeader("vary", "Origin");
  res.setHeader("access-control-allow-methods", "GET,POST,OPTIONS");
  res.setHeader("access-control-allow-headers", "content-type,x-diginx-print-protocol");
}
function json(res, status, value) { res.statusCode=status; res.setHeader("content-type","application/json; charset=utf-8"); res.end(JSON.stringify(value)); }
async function readJson(req, max=2_000_000) {
  const chunks=[]; let size=0;
  for await (const chunk of req) { size += chunk.length; if (size > max) throw new Error("PAYLOAD_TOO_LARGE"); chunks.push(chunk); }
  return JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
}
function requireProtocol(req) { if (req.headers["x-diginx-print-protocol"] !== protocol) throw new Error("PRINT_PROTOCOL_MISMATCH"); }
function printerById(id) { return printers.find((p) => p.printerId === id); }

const server = http.createServer(async (req,res) => {
  const origin = req.headers.origin; cors(res, origin);
  if (!originAllowed(origin)) return json(res,403,{ errorCode:"ORIGIN_NOT_ALLOWED" });
  if (req.method === "OPTIONS") { res.statusCode=204; return res.end(); }
  try {
    requireProtocol(req);
    if (req.method === "GET" && req.url === "/v1/health") return json(res,200,{ status:"ok", version:"0.1.0", printers, recentJobs:recentJobs.slice(-5) });
    if (req.method === "POST" && req.url === "/v1/test-print") {
      const body=await readJson(req,64_000); const printer=printerById(body.printerId);
      if (!printer) return json(res,404,{ status:"failed", errorCode:"PRINTER_NOT_FOUND" });
      if (process.env.DIGINX_PRINT_BRIDGE_FAIL === "1") return json(res,503,{ status:"failed", errorCode:"PRINTER_UNAVAILABLE" });
      const job={ kind:"test", printerId:printer.printerId, processedAt:new Date().toISOString() }; recentJobs.push(job);
      console.log(`[DigiNx Print Bridge] TEST -> ${printer.label}`); return json(res,200,{ status:"printed", ...job });
    }
    if (req.method === "POST" && req.url === "/v1/print") {
      const body=await readJson(req); const printer=printerById(body.printerId);
      if (!printer) return json(res,404,{ status:"failed", errorCode:"PRINTER_NOT_FOUND", commandId:body.commandId, receiptId:body.receiptId });
      if (!body.commandId || !body.receiptId || !body.receiptNumber || !body.printableHtml) return json(res,400,{ status:"failed", errorCode:"INVALID_PRINT_COMMAND" });
      if (process.env.DIGINX_PRINT_BRIDGE_FAIL === "1") return json(res,503,{ status:"failed", errorCode:"PRINTER_UNAVAILABLE", commandId:body.commandId, receiptId:body.receiptId });
      const job={ kind:"receipt", commandId:body.commandId, receiptId:body.receiptId, receiptNumber:body.receiptNumber, printerId:printer.printerId, copies:Math.max(1,Number(body.copies)||1), processedAt:new Date().toISOString() };
      recentJobs.push(job); if(recentJobs.length>100) recentJobs.splice(0,recentJobs.length-100);
      console.log(`[DigiNx Print Bridge] ${body.receiptNumber} -> ${printer.label} x${job.copies}`);
      // Foundation bridge acknowledges an authenticated/validated local spool request. Hardware drivers plug in here.
      return json(res,200,{ status:"printed", ...job });
    }
    return json(res,404,{ errorCode:"NOT_FOUND" });
  } catch (error) { return json(res,error?.message === "PAYLOAD_TOO_LARGE" ? 413 : 400,{ status:"failed", errorCode:error instanceof Error ? error.message : "BRIDGE_ERROR" }); }
});
server.listen(PORT,HOST,()=>console.log(`[DigiNx Print Bridge] listening on http://${HOST}:${PORT}`));
