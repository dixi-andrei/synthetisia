using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Synesthesia.Web.Areas.Identity.Pages.Account;
using Synesthesia.Web.Models;
using Xunit;

namespace Synesthesia.Web.Tests
{
    public class AuthenticationTests
    {
        private Mock<UserManager<AppUser>> MockUserManager()
        {
            var store = new Mock<IUserStore<AppUser>>();
            return new Mock<UserManager<AppUser>>(
                store.Object, null, null, null, null, null, null, null, null
            );
        }

        private Mock<SignInManager<AppUser>> MockSignInManager(Mock<UserManager<AppUser>> userManager)
        {
            var contextAccessor = new Mock<IHttpContextAccessor>();
            var claimsFactory = new Mock<IUserClaimsPrincipalFactory<AppUser>>();
            return new Mock<SignInManager<AppUser>>(
                userManager.Object,
                contextAccessor.Object,
                claimsFactory.Object,
                null, null, null, null
            );
        }

        #region Login Tests

        [Fact]
        public async Task Login_ValidCredentials_RedirectsToReturnUrl()
        {
            // Arrange
            var userManager = MockUserManager();
            var signInManager = MockSignInManager(userManager);
            var logger = new Mock<ILogger<LoginModel>>();

            signInManager
                .Setup(sm => sm.PasswordSignInAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>()))
                .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

            var model = new LoginModel(signInManager.Object, logger.Object)
            {
                Input = new LoginModel.InputModel
                {
                    Email = "test@test.com",
                    Password = "Test123!",
                    RememberMe = false
                }
            };

            // Act
            var result = await model.OnPostAsync("/Studio");

            // Assert
            var redirectResult = Assert.IsType<LocalRedirectResult>(result);
            Assert.Equal("/Studio", redirectResult.Url);
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsPageWithError()
        {
            // Arrange
            var userManager = MockUserManager();
            var signInManager = MockSignInManager(userManager);
            var logger = new Mock<ILogger<LoginModel>>();

            signInManager
                .Setup(sm => sm.PasswordSignInAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>()))
                .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

            var model = new LoginModel(signInManager.Object, logger.Object)
            {
                Input = new LoginModel.InputModel
                {
                    Email = "wrong@test.com",
                    Password = "WrongPassword",
                    RememberMe = false
                }
            };

            // Act
            var result = await model.OnPostAsync();

            // Assert
            Assert.IsType<PageResult>(result);
            Assert.False(model.ModelState.IsValid);
            Assert.Contains("Invalid login attempt",
                model.ModelState[string.Empty]?.Errors[0].ErrorMessage ?? "");
        }

        [Fact]
        public async Task Login_LockedOutAccount_RedirectsToLockout()
        {
            // Arrange
            var userManager = MockUserManager();
            var signInManager = MockSignInManager(userManager);
            var logger = new Mock<ILogger<LoginModel>>();

            signInManager
                .Setup(sm => sm.PasswordSignInAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>()))
                .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.LockedOut);

            var model = new LoginModel(signInManager.Object, logger.Object)
            {
                Input = new LoginModel.InputModel
                {
                    Email = "locked@test.com",
                    Password = "Test123!",
                    RememberMe = false
                }
            };

            // Act
            var result = await model.OnPostAsync();

            // Assert
            var redirectResult = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("./Lockout", redirectResult.PageName);
        }

        #endregion

        #region Register Tests

        [Fact]
        public async Task Register_ValidInput_CreatesUserAndRedirects()
        {
            // Arrange
            var userManager = MockUserManager();
            var signInManager = MockSignInManager(userManager);
            var logger = new Mock<ILogger<RegisterModel>>();
            var emailSender = new Mock<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();

            var userStore = new Mock<IUserStore<AppUser>>();
            var emailStore = userStore.As<IUserEmailStore<AppUser>>();

            userManager
                .Setup(um => um.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Success);

            userManager
                .Setup(um => um.GetUserIdAsync(It.IsAny<AppUser>()))
                .ReturnsAsync("new-user-id");

            userManager
                .Setup(um => um.GenerateEmailConfirmationTokenAsync(It.IsAny<AppUser>()))
                .ReturnsAsync("fake-token");

            signInManager
                .Setup(sm => sm.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), null))
                .Returns(Task.CompletedTask);

            var model = new RegisterModel(
                userManager.Object,
                userStore.Object,
                signInManager.Object,
                logger.Object,
                emailSender.Object)
            {
                Input = new RegisterModel.InputModel
                {
                    Email = "newuser@test.com",
                    Password = "NewPass123!",
                    ConfirmPassword = "NewPass123!"
                }
            };

            // Act
            var result = await model.OnPostAsync();

            // Assert
            var redirectResult = Assert.IsType<LocalRedirectResult>(result);
            Assert.Equal("~/", redirectResult.Url);

            userManager.Verify(um => um.CreateAsync(
                It.Is<AppUser>(u => u.Email == "newuser@test.com"),
                "NewPass123!"), Times.Once);
        }

        [Fact]
        public async Task Register_DuplicateEmail_ReturnsPageWithError()
        {
            // Arrange
            var userManager = MockUserManager();
            var signInManager = MockSignInManager(userManager);
            var logger = new Mock<ILogger<RegisterModel>>();
            var emailSender = new Mock<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();
            var userStore = new Mock<IUserStore<AppUser>>();

            var errors = new[]
            {
                new IdentityError { Code = "DuplicateEmail", Description = "Email already exists" }
            };

            userManager
                .Setup(um => um.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Failed(errors));

            var model = new RegisterModel(
                userManager.Object,
                userStore.Object,
                signInManager.Object,
                logger.Object,
                emailSender.Object)
            {
                Input = new RegisterModel.InputModel
                {
                    Email = "existing@test.com",
                    Password = "Pass123!",
                    ConfirmPassword = "Pass123!"
                }
            };

            // Act
            var result = await model.OnPostAsync();

            // Assert
            Assert.IsType<PageResult>(result);
            Assert.False(model.ModelState.IsValid);
            Assert.Contains("Email already exists",
                model.ModelState[string.Empty]?.Errors[0].ErrorMessage ?? "");
        }

        [Fact]
        public async Task Register_WeakPassword_ReturnsPageWithError()
        {
            // Arrange
            var userManager = MockUserManager();
            var signInManager = MockSignInManager(userManager);
            var logger = new Mock<ILogger<RegisterModel>>();
            var emailSender = new Mock<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();
            var userStore = new Mock<IUserStore<AppUser>>();

            var errors = new[]
            {
                new IdentityError
                {
                    Code = "PasswordTooShort",
                    Description = "Password must be at least 6 characters"
                }
            };

            userManager
                .Setup(um => um.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Failed(errors));

            var model = new RegisterModel(
                userManager.Object,
                userStore.Object,
                signInManager.Object,
                logger.Object,
                emailSender.Object)
            {
                Input = new RegisterModel.InputModel
                {
                    Email = "test@test.com",
                    Password = "123",
                    ConfirmPassword = "123"
                }
            };

            // Act
            var result = await model.OnPostAsync();

            // Assert
            Assert.IsType<PageResult>(result);
            Assert.False(model.ModelState.IsValid);
        }

        #endregion

        #region Logout Tests

        [Fact]
        public async Task Logout_ValidRequest_SignsOutAndRedirects()
        {
            // Arrange
            var userManager = MockUserManager();
            var signInManager = MockSignInManager(userManager);
            var logger = new Mock<ILogger<LogoutModel>>();

            signInManager
                .Setup(sm => sm.SignOutAsync())
                .Returns(Task.CompletedTask);

            var model = new LogoutModel(signInManager.Object, logger.Object);

            // Act
            var result = await model.OnPost();

            // Assert
            var redirectResult = Assert.IsType<RedirectToPageResult>(result);
            signInManager.Verify(sm => sm.SignOutAsync(), Times.Once);
        }

        [Fact]
        public async Task Logout_WithReturnUrl_RedirectsToUrl()
        {
            // Arrange
            var userManager = MockUserManager();
            var signInManager = MockSignInManager(userManager);
            var logger = new Mock<ILogger<LogoutModel>>();

            signInManager
                .Setup(sm => sm.SignOutAsync())
                .Returns(Task.CompletedTask);

            var model = new LogoutModel(signInManager.Object, logger.Object);

            // Act
            var result = await model.OnPost("/Index");

            // Assert
            var redirectResult = Assert.IsType<LocalRedirectResult>(result);
            Assert.Equal("/Index", redirectResult.Url);
        }

        #endregion
    }
}