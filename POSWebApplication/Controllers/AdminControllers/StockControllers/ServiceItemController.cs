﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using POSinCloud.Services;
using POSWebApplication.Data;
using POSWebApplication.Models;

namespace POSWebApplication.Controllers.AdminControllers.StockControllers
{
    [Authorize]
    public class ServiceItemController : Controller
    {
        private readonly POSWebAppDbContext _dbContext;

        public ServiceItemController(DatabaseServices dbServices, IHttpContextAccessor accessor)
        {
            var connection = accessor.HttpContext?.Session.GetString("Connection") ?? "";
            if (connection.IsNullOrEmpty())
            {
                accessor.HttpContext?.Response.Redirect("../SystemSettings/Index");
            }
            else
            {
                _dbContext = new POSWebAppDbContext(dbServices.ConnectDatabase(connection));
            }
        }

        #region // Main methods //

        public async Task<IActionResult> Index()
        {
            SetLayOutData();

            if (TempData["info message"] != null)
            {
                ViewBag.InfoMessage = TempData["info message"];
            }
            if (TempData["warning message"] != null)
            {
                ViewBag.WarningMessage = TempData["warning message"];
            }
            var items = await _dbContext.ms_serviceitem.ToListAsync();

            var itemModelList = new ServiceItemModelList()
            {
                ServiceItems = items
            };

            return View(itemModelList);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ServiceItem serviceItem)
        {
            if (ModelState.IsValid)
            {
                var userCde = HttpContext.User.Claims.FirstOrDefault()?.Value;
                serviceItem.UserId = _dbContext.ms_user.FirstOrDefault(u => u.UserCde == userCde)?.UserId;
                serviceItem.CreatedDtetime = DateTime.Now;
                _dbContext.Add(serviceItem);
                await _dbContext.SaveChangesAsync();
                TempData["info message"] = "Service Item is successfully created.";
            }
            else
            {
                TempData["warning message"] = "Required fields must be filled";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ServiceItem serviceItem)
        {
            if (ModelState.IsValid)
            {
                _dbContext.ms_serviceitem.Update(serviceItem);
                await _dbContext.SaveChangesAsync();
                TempData["info message"] = "Service Item is successfully updated!";
            }
            else
            {
                TempData["warning message"] = "Required fields must be filled.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(ServiceItem serviceItem)
        {
            var dbServiceItem = await _dbContext.ms_serviceitem.FindAsync(serviceItem.SrvcId);
            if (dbServiceItem != null)
            {
                _dbContext.ms_serviceitem.Remove(dbServiceItem);
            }

            await _dbContext.SaveChangesAsync();
            TempData["info message"] = "Service Item is successfully deleted.";
            return RedirectToAction(nameof(Index));
        }

        #endregion


        #region // JS methods //

        public async Task<IActionResult> AddServiceItemPartial()
        {
            var items = await _dbContext.ms_serviceitem.ToListAsync();
            var categories = await _dbContext.ms_stockcategory.ToListAsync();
            var groups = await _dbContext.ms_stockgroup.ToListAsync();
            var stockUOMs = await _dbContext.ms_stockuom.ToListAsync();

            var itemModelList = new ServiceItemModelList()
            {
                ServiceItems = items,
                StockCategories = categories,
                StockGroups = groups,
                StockUOMs = stockUOMs
            };

            return PartialView("_AddServiceItemPartial", itemModelList);
        }

        public async Task<IActionResult> EditServiceItemPartial(int srvcId)
        {
            var items = await _dbContext.ms_serviceitem.ToListAsync();
            var categories = await _dbContext.ms_stockcategory.ToListAsync();
            var groups = await _dbContext.ms_stockgroup.ToListAsync();
            var stockUOMs = await _dbContext.ms_stockuom.ToListAsync();
            var serviceItem = await _dbContext.ms_serviceitem.FindAsync(srvcId);

            var itemModelList = new ServiceItemModelList()
            {
                ServiceItems = items,
                StockCategories = categories,
                StockGroups = groups,
                StockUOMs = stockUOMs,
                ServiceItem = serviceItem
            };

            return PartialView("_EditServiceItemPartial", itemModelList);
        }

        public async Task<IActionResult> DeleteServiceItemPartial(int srvcId)
        {
            var items = await _dbContext.ms_serviceitem.ToListAsync();

            var serviceItem = await _dbContext.ms_serviceitem.FindAsync(srvcId);

            var itemModelList = new ServiceItemModelList()
            {
                ServiceItems = items,
                ServiceItem = serviceItem
            };

            return PartialView("_DeleteServiceItemPartial", itemModelList);
        }

        #endregion


        #region // Common methods //
        protected void SetLayOutData()
        {
            var userCde = HttpContext.User.Claims.FirstOrDefault()?.Value;
            var user = _dbContext.ms_user.FirstOrDefault(u => u.UserCde == userCde);

            if (user != null)
            {
                ViewData["Username"] = user.UserNme;

                var accLevel = _dbContext.ms_usermenuaccess.FirstOrDefault(u => u.MnuGrpId == user.MnuGrpId)?.AccLevel;
                ViewData["User Role"] = accLevel.HasValue ? $"accessLvl{accLevel}" : null;

                var posId = _dbContext.ms_userpos
                    .Where(pos => pos.UserId == user.UserId)
                    .Select(pos => pos.POSid)
                    .FirstOrDefault();

                var company = _dbContext.ms_autonumber
                    .Where(auto => auto.PosId == posId)
                    .FirstOrDefault();

                if (company != null)
                {
                    ViewData["Business Date"] = company.BizDte.ToString("dd-MM-yyyy");

                    var dbName = HttpContext.Session.GetString("Database");
                    ViewData["Database"] = $"{dbName}({company.POSPkgNme})";
                }

            }
        }

        #endregion
    }
}
