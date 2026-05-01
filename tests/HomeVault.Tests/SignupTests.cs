/*
 * FILE: SignupTests.cs
 * PROJECT: HomeVault.Tests
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: Verifies that signup rejects duplicate usernames and adds
 *              a model error rather than creating a second account.
 */

using HomeVault.Controllers;
using HomeVault.Data;
using HomeVault.Models.Entities;
using HomeVault.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HomeVault.Tests
{
    public class SignupTests
    {
        /*
         * Function: BuildContextWithExistingUser()
         * Description: In-memory context preloaded with one user named
         *              "alice" so we can test duplicate detection.
         * Parameter: none.
         * Return: CatalogDbContext - the seeded context.
         */
        private static CatalogDbContext BuildContextWithExistingUser()
        {
            DbContextOptions<CatalogDbContext> options =
                new DbContextOptionsBuilder<CatalogDbContext>()
                    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                    .Options;

            CatalogDbContext context = new CatalogDbContext(options);

            context.Catalogs.Add(new Catalog { CatalogId = "catA" });
            context.Residents.Add(new Resident
            {
                ResidentId = "rA",
                ResidentName = "Alice",
                ResidentAddress = "1 Main St",
                CatalogId = "catA"
            });
            context.ResidentUsers.Add(new ResidentUser
            {
                ResidentId = "rA",
                Username = "alice",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo123!")
            });
            context.SaveChanges();

            return context;
        }

        /*
         * Function: BuildController(CatalogDbContext)
         * Description: Wires up an AccountController with a mock logger
         *              and the supplied EF context.
         * Parameter: CatalogDbContext context - the test context.
         * Return: AccountController - configured controller.
         */
        private static AccountController BuildController(CatalogDbContext context)
        {
            ILogger<AccountController> logger = new Mock<ILogger<AccountController>>().Object;
            AccountController controller = new AccountController(context, logger);

            // Minimum HttpContext so TempData / ModelState work.
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.TempData = new TempDataDictionary(
                controller.HttpContext,
                new Mock<ITempDataProvider>().Object);

            return controller;
        }

        /*
         * Function: Signup_RejectsDuplicateUsername()
         * Description: Submits a signup form using an already-taken
         *              username and asserts the controller returns the
         *              Signup view with a "Username already exists." error.
         * Parameter: none.
         * Return: Task (async test).
         */
        [Fact]
        public async Task Signup_RejectsDuplicateUsername()
        {
            CatalogDbContext context = BuildContextWithExistingUser();
            AccountController controller = BuildController(context);

            SignupViewModel model = new SignupViewModel
            {
                ResidentName = "Alice 2",
                ResidentAddress = "2 Side St",
                Username = "alice",
                Password = "Demo123!",
                ConfirmPassword = "Demo123!"
            };

            IActionResult actionResult = await controller.Signup(model);

            ViewResult viewResult = Assert.IsType<ViewResult>(actionResult);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(controller.ModelState[nameof(model.Username)]!.Errors,
                e => e.ErrorMessage == "Username already exists.");

            // Verify no duplicate user was inserted.
            int userCount = context.ResidentUsers.Count(u => u.Username == "alice");
            Assert.Equal(1, userCount);
        }

        /*
         * Function: Signup_CreatesAccount_WhenInputIsValid()
         * Description: Submits a valid signup form and asserts the new
         *              Catalog, Resident, and ResidentUser rows are written
         *              and the controller redirects to the Login page.
         * Parameter: none.
         * Return: Task (async test).
         */
        [Fact]
        public async Task Signup_CreatesAccount_WhenInputIsValid()
        {
            CatalogDbContext context = BuildContextWithExistingUser();
            AccountController controller = BuildController(context);

            SignupViewModel model = new SignupViewModel
            {
                ResidentName = "Bob",
                ResidentAddress = "3 Other St",
                Username = "bob",
                Password = "Demo456!",
                ConfirmPassword = "Demo456!"
            };

            IActionResult actionResult = await controller.Signup(model);

            RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(actionResult);
            Assert.Equal("Login", redirect.ActionName);
            Assert.True(context.ResidentUsers.Any(u => u.Username == "bob"));
        }
    }
}