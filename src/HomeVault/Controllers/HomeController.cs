/*
 * FILE: HomeController.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-07
 * DESCRIPTION: Provides actions for the public landing pages of HomeVault
 *              (Home, Privacy, Error). Index and Privacy are anonymous so
 *              they can serve as marketing pages; Error is reachable from
 *              the global exception handler.
 */

using System.Diagnostics;
using HomeVault.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeVault.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        /*
         * Function: HomeController(ILogger<HomeController> logger)
         * Description: Constructor. Captures the framework-supplied logger.
         * Parameter: ILogger<HomeController> logger - logger instance.
         * Return: none (constructor).
         */
        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        /*
         * Function: Index()
         * Description: Renders the public landing page.
         * Parameter: none.
         * Return: IActionResult result - the Index view.
         */
        [Authorize]
        public IActionResult Index()
        {
            IActionResult result = View();
            return result;
        }

        /*
         * Function: Privacy()
         * Description: Renders the privacy policy page.
         * Parameter: none.
         * Return: IActionResult result - the Privacy view.
         */
        [AllowAnonymous]
        public IActionResult Privacy()
        {
            IActionResult result = View();
            return result;
        }

        /*
         * Function: Error()
         * Description: Renders an unhandled-exception error page with the
         *              current request id for traceability.
         * Parameter: none.
         * Return: IActionResult result - the Error view bound to an
         *         ErrorViewModel populated with the current request id.
         */
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            ErrorViewModel model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            };
            IActionResult result = View(model);
            return result;
        }
    }
}