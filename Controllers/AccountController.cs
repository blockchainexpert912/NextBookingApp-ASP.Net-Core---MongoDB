﻿using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using NextLevelTrainingApi.DAL.Entities;
using NextLevelTrainingApi.DAL.Interfaces;
using NextLevelTrainingApi.Helper;
using NextLevelTrainingApi.Models;
using NextLevelTrainingApi.ViewModels;

namespace NextLevelTrainingApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private IUnitOfWork _unitOfWork;
        private readonly JWTAppSettings _jwtAppSettings;
        private EmailSettings _emailSettings;
        public AccountController(IUnitOfWork unitOfWork, IOptions<JWTAppSettings> jwtAppSettings, IOptions<EmailSettings> emailSettings)
        {
            _unitOfWork = unitOfWork;
            _jwtAppSettings = jwtAppSettings.Value;
            _emailSettings = emailSettings.Value;
        }
        [HttpPost]
        [Route("Register")]
        public ActionResult<Users> Register(UserViewModel userVM)
        {

            Users user = _unitOfWork.UserRepository.FindOne(x => x.EmailID == userVM.EmailID);
            if (user != null)
            {
                return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "UserName already registered." } } });
            }

            //return userVM;


            user = new Users()
            {
                Id = Guid.NewGuid(),
                Address = userVM.Address,
                State = userVM.State,
                EmailID = userVM.EmailID,
                UserName = userVM.UserName,
                FullName = userVM.FullName,
                MobileNo = userVM.MobileNo,
                Role = userVM.Role,
                Password = userVM.Password.Encrypt(),
                Lat = userVM.Lat,
                Lng = userVM.Lng,
                PostCode = userVM.PostCode,
                DeviceID = userVM.DeviceID,
                DeviceType = userVM.DeviceType,
                DeviceToken = userVM.DeviceToken,
                RegisterDate = DateTime.Now,
                Featured = false,
                PaypalPaymentId = ""
            };

            _unitOfWork.UserRepository.InsertOne(user);

            if (user.Role.ToLower() == Constants.COACH)
            {
                EmailHelper.SendEmail(user.EmailID, _emailSettings, "signupcoach");
            }
            else
            {
                EmailHelper.SendEmail(user.EmailID, _emailSettings, "signupplayer");
            }

            string encryptedToken = GenerateToken(user);

            user.Token = encryptedToken;

            return user;
        }


        [HttpGet]
        [Route("DeleteAccount/{email}")]
        public ActionResult<bool> DeleteAccount(string email)
        {
            Users user = _unitOfWork.UserRepository.FindOne(x => x.EmailID.ToLower() == email.ToLower());
            if (user == null)
            {
                return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "Account not found." } } });
            }

            try
            {
                _unitOfWork.LeadsRepository.DeleteMany(x => x.UserId == user.Id);
            }
            catch { }

            try
            {
                _unitOfWork.ResponsesRepository.DeleteMany(x => x.CoachId == user.Id);
            }
            catch { }

            try
            {
                _unitOfWork.PostRepository.DeleteMany(x => x.UserId == user.Id);
            }
            catch { }

            try
            {
                _unitOfWork.CreditHistoryRepository.DeleteMany(x => x.UserId == user.Id);
            }
            catch { }

            try
            {
                if (user.Role.ToLower() == Constants.COACH)
                {
                    _unitOfWork.BookingRepository.DeleteMany(x => x.CoachID == user.Id);
                }
                else
                {
                    _unitOfWork.BookingRepository.DeleteMany(x => x.PlayerID == user.Id);
                }
            }
            catch { }

            try
            {
                _unitOfWork.MessageRepository.DeleteMany(x => x.SenderId == user.Id || x.ReceiverId == user.Id);
            }
            catch { }

            try
            {
                _unitOfWork.NotificationRepository.DeleteMany(x => x.UserId == user.Id);
            }
            catch { }

            _unitOfWork.UserRepository.DeleteById(user.Id);

            return true;
        }


        [HttpGet]
        [Route("SendVerificationEmail/{email}")]
        public ActionResult<string> SendVerificationEmail(string email)
        {
            Users user = _unitOfWork.UserRepository.FindOne(x => x.EmailID.ToLower() == email.ToLower());
            if (user == null)
            {
                return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "EmailID not found." } } });
            }

            var token = GenerateToken(user);

            return token;
        }

        [HttpGet]
        [Route("VerifyEmail/{token}")]
        public ActionResult<bool> VerifyEmail(string token)
        {
            try
            {
                var email = GetEmailFromToken(token);

                Users user = _unitOfWork.UserRepository.FindOne(x => x.EmailID.ToLower() == email.ToLower());

                user.EmailVerified = true;
                _unitOfWork.UserRepository.ReplaceOne(user);

                return true;
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { ex.Message } } });
            }
        }


        [HttpGet]
        [Route("GetUserByEmail/{email}")]
        public ActionResult<Users> GetUserByEmail(string email)
        {

            return _unitOfWork.UserRepository.FindOne(x => x.EmailID.ToLower() == email.ToLower());
        }

        [HttpPost]
        [Route("Login")]
        public ActionResult<string> Login(LoginViewModel userVM)
        {

            var user = new Users();
            user = _unitOfWork.UserRepository.FindOne(x => x.EmailID.ToLower() == userVM.EmailID.ToLower() && x.Password == userVM.Password.Encrypt());
            if(user == null)
                user = _unitOfWork.UserRepository.FindOne(x => x.UserName.ToLower() == userVM.EmailID.ToLower() && x.Password == userVM.Password.Encrypt());

            if (user == null)
            {
                return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "User with these credentials doesn't exist." } } });
            }
            if (!string.IsNullOrEmpty(userVM.DeviceToken) && !string.IsNullOrEmpty(userVM.DeviceType))
            {
                //user.DeviceToken = userVM.DeviceToken;
                user.DeviceType = userVM.DeviceType;
            }
            _unitOfWork.UserRepository.ReplaceOne(user);

            string encryptedToken = GenerateToken(user);

            return encryptedToken;

        }

        private string GenerateToken(Users user)
        {
            // authentication successful so generate jwt token
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwtAppSettings.Secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Name, user.FullName),
                    new Claim(ClaimTypes.Email, user.EmailID),
                    new Claim("UserID", user.Id.ToString())
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            string encryptedToken = tokenHandler.WriteToken(token);
            return encryptedToken;
        }

        [Route("FacebookLogin")]
        [HttpPost]
        public async Task<ActionResult<string>> FacebookLogin(SocialMediaLoginViewModel loginModel)
        {
            var result = await GetAsync<dynamic>(loginModel.AuthenticationToken, "https://graph.facebook.com/v2.8/", "me", "fields=first_name,last_name,email,picture.width(100).height(100)");
            if (result == null)
            {
                return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "No User found or invalid token." } } });
            }

            var fbUserVM = JsonConvert.DeserializeObject<FacebookUserViewModel>(result);

            if (string.IsNullOrEmpty(fbUserVM.Email))
            {
                return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "No EmailID found." } } });
            }

            var user = _unitOfWork.UserRepository.FilterBy(x => x.EmailID.ToLower() == fbUserVM.Email.ToLower()).SingleOrDefault();
            if (user == null)
            {
                user = new Users();
                user.FullName = fbUserVM.FirstName + " " + fbUserVM.LastName;
                user.EmailID = fbUserVM.Email;
                user.Role = loginModel.Role;
                user.PostCode = loginModel.PostCode;
                user.SocialLoginType = Constants.FACEBOOK_LOGIN;
                user.DeviceID = loginModel.DeviceID;
                user.DeviceType = loginModel.DeviceType;
                user.DeviceToken = loginModel.DeviceToken;
                user.Featured = false;
                user.PaypalPaymentId = "";
                if (loginModel.Lat != null)
                {
                    user.Lat = loginModel.Lat;
                }
                if (loginModel.Lng != null)
                {
                    user.Lng = loginModel.Lng;
                }
                if (fbUserVM.Picture != null && fbUserVM.Picture.Data != null)
                {
                    user.ProfileImage = fbUserVM.Picture.Data.Url;
                    user.ProfileImageHeight = fbUserVM.Picture.Data.Height;
                    user.ProfileImageWidth = fbUserVM.Picture.Data.Width;
                }
                _unitOfWork.UserRepository.InsertOne(user);
            }
            else
            {
                if (user.Role.ToLower() != loginModel.Role.ToLower())
                {
                    return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "EmailID already registered." } } });
                }
                user.FullName = fbUserVM.FirstName + " " + fbUserVM.LastName;
                user.EmailID = fbUserVM.Email;
                user.SocialLoginType = Constants.FACEBOOK_LOGIN;
                user.AccessToken = loginModel.AuthenticationToken;
                user.DeviceID = loginModel.DeviceID;
                user.DeviceType = loginModel.DeviceType;
                //user.DeviceToken = loginModel.DeviceToken;
                if (loginModel.Lat != null)
                {
                    user.Lat = loginModel.Lat;
                }
                if (loginModel.Lng != null)
                {
                    user.Lng = loginModel.Lng;
                }
                if (fbUserVM.Picture != null && fbUserVM.Picture.Data != null)
                {
                    user.ProfileImage = fbUserVM.Picture.Data.Url;
                    user.ProfileImageHeight = fbUserVM.Picture.Data.Height;
                    user.ProfileImageWidth = fbUserVM.Picture.Data.Width;
                }
                _unitOfWork.UserRepository.ReplaceOne(user);
            }

            string encryptedToken = GenerateToken(user);

            return encryptedToken;
        }

        private async Task<string> GetAsync<T>(string accessToken, string baseURL, string endpoint, string args = null)
        {
            HttpClient _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseURL)
            };

            _httpClient.DefaultRequestHeaders
                .Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = await _httpClient.GetAsync($"{endpoint}?access_token={accessToken}&{args}");
            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadAsStringAsync();

            return result;
        }


        [Route("GoogleLogin")]
        [HttpPost]
        public ActionResult<string> GoogleLogin(GoogleUserViewModel loginModel)
        {

            var user = _unitOfWork.UserRepository.FilterBy(x => x.EmailID.ToLower() == loginModel.Email.ToLower()).SingleOrDefault();
            if (user == null)
            {
                user = new Users();
                user.FullName = loginModel.Name;
                user.EmailID = loginModel.Email;
                user.PostCode = loginModel.PostCode;
                user.Role = loginModel.Role;
                user.SocialLoginType = Constants.GOOGLE_LOGIN;
                user.AccessToken = loginModel.AuthenticationToken;
                user.DeviceID = loginModel.DeviceID;
                //user.DeviceToken = loginModel.DeviceToken;
                user.DeviceType = loginModel.DeviceType;
                user.Featured = loginModel.Featured;
                if (loginModel.Lat != null)
                {
                    user.Lat = loginModel.Lat;
                }
                if (loginModel.Lng != null)
                {
                    user.Lng = loginModel.Lng;
                }

                user.ProfileImage = loginModel.Picture;
                _unitOfWork.UserRepository.InsertOne(user);
            }
            else
            {
                if (user.Role.ToLower() != loginModel.Role.ToLower())
                {
                    return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "EmailID already registered." } } });
                }
                user.FullName = loginModel.Name;
                user.AccessToken = loginModel.AuthenticationToken;
                user.DeviceID = loginModel.DeviceID;
                //user.DeviceToken = loginModel.DeviceToken;
                user.DeviceType = loginModel.DeviceType;
                if (loginModel.Lat != null)
                {
                    user.Lat = loginModel.Lat;
                }
                if (loginModel.Lng != null)
                {
                    user.Lng = loginModel.Lng;
                }

                user.ProfileImage = loginModel.Picture;
                _unitOfWork.UserRepository.ReplaceOne(user);
            }

            string encryptedToken = GenerateToken(user);

            return encryptedToken;
        }

        [Route("ResetPassword")]
        [HttpPost]
        public ActionResult<bool> ResetPassword(ResetPasswordViewModel loginModel)
        {
            var user = _unitOfWork.UserRepository.FilterBy(x => x.EmailID.ToLower() == loginModel.EmailID.ToLower()).SingleOrDefault();
            if (user == null)
            {
                return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "No User found." } } });
            }
            string pwd = GenerateRandomString();
            user.Password = pwd.Encrypt();
            user.IsTempPassword = true;
            _unitOfWork.UserRepository.ReplaceOne(user);

            var values = new Dictionary<string, string>();
            values.Add("Password", pwd);
            EmailHelper.SendEmail(user.EmailID, _emailSettings, "resetpassword", values);
            return true;
        }


     


        [Route("AppleLogin")]
        [HttpPost]
        public ActionResult<string> AppleLogin(AppleLoginViewModel loginModel)
        {

            var user = _unitOfWork.UserRepository.FilterBy(x => x.EmailID.ToLower() == loginModel.Email.ToLower()).SingleOrDefault();
            if (user == null)
            {
                user = new Users();
                user.FullName = loginModel.Name;
                user.EmailID = loginModel.Email;
                user.PostCode = loginModel.PostCode;
                user.Role = loginModel.Role;
                user.SocialLoginType = Constants.APPLE_LOGIN;
                user.DeviceID = loginModel.DeviceID;
                //user.DeviceToken = loginModel.DeviceToken;
                user.DeviceType = loginModel.DeviceType;
                user.Featured = loginModel.Featured;
                if (loginModel.Lat != null)
                {
                    user.Lat = loginModel.Lat;
                }
                if (loginModel.Lng != null)
                {
                    user.Lng = loginModel.Lng;
                }

                _unitOfWork.UserRepository.InsertOne(user);
            }
            else
            {
                if (user.Role.ToLower() != loginModel.Role.ToLower())
                {
                    return BadRequest(new ErrorViewModel() { errors = new Error() { error = new string[] { "EmailID already registered." } } });
                }
                user.FullName = loginModel.Name;
                user.DeviceID = loginModel.DeviceID;
                user.DeviceToken = loginModel.DeviceToken;
                user.DeviceType = loginModel.DeviceType;
                if (loginModel.Lat != null)
                {
                    user.Lat = loginModel.Lat;
                }
                if (loginModel.Lng != null)
                {
                    user.Lng = loginModel.Lng;
                }
                user.PostCode = loginModel.PostCode;
                _unitOfWork.UserRepository.ReplaceOne(user);
            }

            string encryptedToken = GenerateToken(user);

            return encryptedToken;
        }


        //[Route("GetAllPosts")]
        //[HttpPost]
        //public ActionResult<List<Post>> GetAllPosts()
        //{
        //    var posts = _unitOfWork.PostRepository.AsQueryable().ToList();
        //    foreach(var p in posts)
        //    {
        //        var name = _unitOfWork.UserRepository.FindById(p.UserId);
        //    }
        //    return posts;
        //}
        private string GenerateRandomString()
        {
            Random ran = new Random();

            String b = "abcdefghijklmnopqrstuvwxyz0123456789";
            String sc = "!@#$%^&*~";

            int length = 6;

            String random = "";

            for (int i = 0; i < length; i++)
            {
                int a = ran.Next(b.Length); //string.Lenght gets the size of string
                random = random + b.ElementAt(a);
            }
            for (int j = 0; j < 2; j++)
            {
                int sz = ran.Next(sc.Length);
                random = random + sc.ElementAt(sz);
            }
            return random;
        }

        private string GetEmailFromToken(string token)
        {
            TokenValidationParameters validationParameters = new TokenValidationParameters
            {
                ValidateLifetime = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_jwtAppSettings.Secret)),
            };
            ClaimsPrincipal principal = new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out _);

            return principal?.FindFirst(ClaimTypes.Email)?.Value;
        }
    }
}