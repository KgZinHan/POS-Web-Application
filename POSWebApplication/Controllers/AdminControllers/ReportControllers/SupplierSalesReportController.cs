﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Reporting.NETCore;
using POSWebApplication.Data;
using POSWebApplication.Models;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing;
using System.Text;
using Rectangle = System.Drawing.Rectangle;
using Microsoft.AspNetCore.Authorization;

namespace POSWebApplication.Controllers.AdminControllers.ReportControllers
{
    [Authorize]
    public class SupplierSalesReportController : Controller
    {
        private readonly POSWebAppDbContext _dbContext;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private static List<Stream> m_streams;
        private static int m_currentPageIndex = 0;
        public SupplierSalesReportController(POSWebAppDbContext dbContext, IWebHostEnvironment webHostEnvironment)
        {
            _dbContext = dbContext;
            _webHostEnvironment = webHostEnvironment;
        }
        public IActionResult Index()
        {
            SetLayOutData();

            var allStocks = _dbContext.ms_stock
                   .Select(stock => new Stock
                   {
                       ItemId = stock.ItemId,
                       ItemDesc = stock.ItemDesc,
                       CatgCde = stock.CatgCde
                   })
                   .Union(_dbContext.ms_serviceitem
                   .Select(serviceItem => new Stock
                   {
                       ItemId = serviceItem.SrvcItemId,
                       ItemDesc = serviceItem.SrvcDesc,
                       CatgCde = serviceItem.CategoryId
                   }));
            ViewData["ItemList"] = new SelectList(allStocks, "ItemId", "ItemDesc");

            var supplierSalesReport = new SupplierSalesReport()
            {
                FromDateTime = GetBizDte(),
                ToDateTime = GetBizDte()
            };

            return View(supplierSalesReport);
        }

        public IActionResult PrintReview(SupplierSalesReport supplierSalesReport)
        {
            var billHQuery = _dbContext.billh.AsQueryable();

            billHQuery = billHQuery.Where(l => l.BizDte >= supplierSalesReport.FromDateTime && l.BizDte <= supplierSalesReport.ToDateTime);

            var allStocks = _dbContext.ms_stock
                    .Select(stock => new Stock
                    {
                        ItemId = stock.ItemId,
                        ItemDesc = stock.ItemDesc,
                        CatgCde = stock.CatgCde
                    })
                    .Union(_dbContext.ms_serviceitem
                    .Select(serviceItem => new Stock
                    {
                        ItemId = serviceItem.SrvcItemId,
                        ItemDesc = serviceItem.SrvcDesc,
                        CatgCde = serviceItem.CategoryId
                    }));

            var result = billHQuery
                .Where(h => h.Status == 'P')
                .Join(_dbContext.billd,
                    h => h.BillhId,
                    d => d.BillhId,
                    (h, d) => new
                    {
                        posid = h.POSId,
                        locCde = h.LocCde,
                        bizDte = h.BizDte,
                        qty = d.Qty,
                        price = d.Price,
                        discAmt = d.DiscAmt,
                        itemId = d.ItemID,
                        voidFlg = d.VoidFlg
                    })
                .Where(d => d.voidFlg == false)
                .Join(allStocks,
                    r => r.itemId,
                    s => s.ItemId,
                    (r, s) => new
                    {
                        r.posid,
                        r.locCde,
                        r.bizDte,
                        r.qty,
                        r.price,
                        r.discAmt,
                        r.itemId,
                        catgCde = s.CatgCde
                    })
                .GroupBy(group => new { group.posid, group.bizDte, group.catgCde })
                .Select(group => new
                {
                    group.Key.posid,
                    bizdte = group.Key.bizDte.ToString("dd-MMM-yyyy"),
                    remark = group.Key.catgCde,
                    cmpyid = group.Sum(x => x.qty),
                    taxamt = group.Sum(x => (x.qty * x.price) - x.discAmt),
                    billdiscount = group.Sum(x => x.discAmt)
                })
                .OrderBy(group => group.remark)
                .ToList();



            if (supplierSalesReport.Supplier != null)
            {
                result = result.Where(item => supplierSalesReport.Supplier.Contains(item.remark)).ToList();
            }

            try
            {
                var report = new LocalReport();
                var path = $"{this._webHostEnvironment.WebRootPath}\\Report\\SupplierSalesReport.rdlc";
                report.ReportPath = path;
                report.DataSources.Add(new ReportDataSource("DataSet1", result));
                report.SetParameters(new[] {
                    new ReportParameter("FromDateTime", supplierSalesReport.FromDateTime.ToString("dd-MMM-yyyy")),
                    new ReportParameter("ToDateTime", supplierSalesReport.ToDateTime.ToString("dd-MMM-yyyy")),
                 });
                var pdfBytes = report.Render("PDF");
                return File(pdfBytes, "application/pdf");
                //PrintToPrinter(report);
                //return RedirectToAction("Index");
            }
            catch (Exception e)
            {
                return BadRequest("An error occurred while generating the report. Please try again later.");
            }
        }

        protected DateTime GetBizDte()
        {
            var userCde = HttpContext.User.Claims.FirstOrDefault()?.Value;
            var user = _dbContext.ms_user.FirstOrDefault(u => u.UserCde == userCde);
            var POS = _dbContext.ms_userpos.FirstOrDefault(pos => pos.UserId == user.UserId);

            var bizDte = _dbContext.ms_autonumber
                .Where(auto => auto.PosId == POS.POSid)
                .Select(auto => auto.BizDte)
                .FirstOrDefault();

            return bizDte;
        }

        protected void SetLayOutData()
        {
            var userCde = HttpContext.User.Claims.FirstOrDefault()?.Value;
            var user = _dbContext.ms_user.FirstOrDefault(u => u.UserCde == userCde);

            if (user != null)
            {
                ViewData["Username"] = user.UserNme;

                var accLevel = _dbContext.ms_usermenuaccess.FirstOrDefault(u => u.MnuGrpId == user.MnuGrpId)?.AccLevel;
                ViewData["User Role"] = accLevel.HasValue ? $"accessLvl{accLevel}" : null;

                var POS = _dbContext.ms_userpos.FirstOrDefault(pos => pos.UserId == user.UserId);

                var bizDte = _dbContext.ms_autonumber
                    .Where(auto => auto.PosId == POS.POSid)
                    .Select(auto => auto.BizDte)
                    .FirstOrDefault();

                ViewData["Business Date"] = bizDte.ToString("dd MMM yyyy");
            }
        }


        #region Direct Print Methods

        public static void PrintToPrinter(LocalReport report)
        {
            Export(report);

        }

        public static void Export(LocalReport report, bool print = true)
        {
            string deviceInfo =
             @"<DeviceInfo>
                <OutputFormat>EMF</OutputFormat>
                <PageWidth>12in</PageWidth>
                <PageHeight>8in</PageHeight>
                <MarginTop>0in</MarginTop>
                <MarginLeft>0.1in</MarginLeft>
                <MarginRight>0.1in</MarginRight>
                <MarginBottom>0in</MarginBottom>
            </DeviceInfo>";
            Warning[] warnings;
            m_streams = new List<Stream>();
            report.Render("Image", deviceInfo, CreateStream, out warnings);
            foreach (Stream stream in m_streams)
                stream.Position = 0;

            if (print)
            {
                Print();
            }
        }

        public static void Print()
        {
            if (m_streams == null || m_streams.Count == 0)
                throw new Exception("Error: no stream to print.");
            PrintDocument printDoc = new PrintDocument();
            if (!printDoc.PrinterSettings.IsValid)
            {
                throw new Exception("Error: cannot find the default printer.");
            }
            else
            {
                printDoc.PrintPage += new PrintPageEventHandler(PrintPage);
                m_currentPageIndex = 0;
                printDoc.Print();
            }
        }

        public static Stream CreateStream(string name, string fileNameExtension, Encoding encoding, string mimeType, bool willSeek)
        {
            Stream stream = new MemoryStream();
            m_streams.Add(stream);
            return stream;
        }

        public static void PrintPage(object sender, PrintPageEventArgs ev)
        {
            Metafile pageImage = new
               Metafile(m_streams[m_currentPageIndex]);

            // Adjust rectangular area with printer margins.
            Rectangle adjustedRect = new Rectangle(
                ev.PageBounds.Left - (int)ev.PageSettings.HardMarginX,
                ev.PageBounds.Top - (int)ev.PageSettings.HardMarginY,
                ev.PageBounds.Width,
                ev.PageBounds.Height);

            // Draw a white background for the report
            ev.Graphics.FillRectangle(Brushes.White, adjustedRect);

            // Draw the report content
            ev.Graphics.DrawImage(pageImage, adjustedRect);

            // Prepare for the next page. Make sure we haven't hit the end.
            m_currentPageIndex++;
            ev.HasMorePages = (m_currentPageIndex < m_streams.Count);
        }

        public static void DisposePrint()
        {
            if (m_streams != null)
            {
                foreach (Stream stream in m_streams)
                    stream.Close();
                m_streams = null;
            }
        }


        #endregion 

    }
}
