using BaseLibrary.DTOs;
using BaseLibrary.Entities;
using BaseLibrary.Responses;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using ServerLibrary.Data;
using ServerLibrary.Helpers;
using ServerLibrary.Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Constants = ServerLibrary.Helpers.Constants;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace ServerLibrary.Repositories.Implementations
{
    public class UserAccountRepository(IOptions<JwtSection> config, AppDbContext appDbContext) : IUserAccount
    {
        public async Task<GeneralResponse> CreateAsync(Register user)
        {
            if (user is null) return new GeneralResponse(false, "Model is empty");

            var checkUser = FindUserByEmail(user.Email!);
            if (checkUser != null) return new GeneralResponse(false, "User registered already");

            //Save user
            var applicationUser = await AddToDataBase(new ApplicationUser()
            {
                FullName = user.FullName,
                Email = user.Email,
                Password = BCrypt.Net.BCrypt.HashPassword(user.Password)
            });

            //check, create and assign role
            var checkAdminRole = appDbContext.SystemRoles.FirstOrDefault(_ => _.Name!.Equals(Constants.Admin));
            if(checkAdminRole is null)
            {
                var createAdminRole = await AddToDataBase(new SystemRole() { Name = Constants.Admin });
                await AddToDataBase(new UserRole() { RoleId = createAdminRole.Id, UserId = applicationUser.Id });
                return new GeneralResponse(true, "Account created!");
            }

            var checkUserRole = await appDbContext.SystemRoles.FirstOrDefaultAsync(_ => _.Name!.Equals(Constants.User));
            SystemRole response = new();
            if (checkUserRole is null)
            {
                response = await AddToDataBase(new SystemRole() { Name = Constants.User });
                await AddToDataBase(new UserRole() { RoleId = response.Id, UserId = applicationUser.Id });
            }
            else
            {
                await AddToDataBase(new UserRole() {  RoleId = checkUserRole.Id, UserId=applicationUser.Id });
            }
            return new GeneralResponse(true, "Account created");
        }

        public async Task<LoginResponse> SignInAsync(Login user)
        {
            if (user is null) return new LoginResponse(false, "Model is empty");

            var applicationUser = await FindUserByEmail(user.Email);
            if (applicationUser is null) return new LoginResponse(false, "User not found");

            //Verify Password
            if (!BCrypt.Net.BCrypt.Verify(user.Password, applicationUser.Password))
                return new LoginResponse(false, "E-mail / Password invalid");

            var getUserRole = await appDbContext.UserRoles.FirstOrDefaultAsync(_ => _.UserId == applicationUser.Id);
            if (getUserRole is null) return new LoginResponse(false, "User role not found");

            var getRoleName = await appDbContext.SystemRoles.FirstOrDefaultAsync(_ => _.Id == getUserRole.RoleId);
            if (getRoleName is null) return new LoginResponse(false, "User role not found");

            string jwtToken = GenerateToken(applicationUser, getRoleName!.Name!);
            string refreshToken = GenerateRefreshToken();
            return new LoginResponse(true, "Login sucessfully!", jwtToken, refreshToken);
        }

        private string GenerateToken(ApplicationUser user, string role)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Value.Key!));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var userClaims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName!),
                new Claim(ClaimTypes.Email, user.Email!),
                new Claim(ClaimTypes.Role, role!)
            };

            var token = new JwtSecurityToken(
                issuer: config.Value.Issuer,
                audience: config.Value.Audience,
                claims: userClaims,
                expires: DateTime.Now.AddDays(1),
                signingCredentials: credentials
                );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task <ApplicationUser> FindUserByEmail(string email) =>
            await appDbContext.AplicationUsers.FirstOrDefaultAsync(_ => _.Email!.ToLower()!.Equals(email!.ToLower()));
        
        private async Task<T> AddToDataBase<T>(T model)
        {
            var result = appDbContext.Add(model!);
            await appDbContext.SaveChangesAsync();
            return (T)result.Entity;
        }
    }
}
