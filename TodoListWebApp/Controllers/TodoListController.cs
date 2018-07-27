﻿/*
 The MIT License (MIT)

Copyright (c) 2015 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using TodoListWebApp.Models;

namespace TodoListWebApp.Controllers
{
    [Authorize]
    public class TodoListController : Controller
    {
        //
        // The Client ID is used by the application to uniquely identify itself to Azure AD.
        // The App Key is a credential used by the application to authenticate to Azure AD.
        // The Tenant is the name of the Azure AD tenant in which this application is registered.
        // The AAD Instance is the instance of Azure, for example public Azure or Azure China.
        // The Authority is the sign-in URL of the tenant.
        //
        private static string aadInstance = ConfigurationManager.AppSettings["ida:AADInstance"];
        private static string tenant = ConfigurationManager.AppSettings["ida:Tenant"];
        private static string clientId = ConfigurationManager.AppSettings["ida:ClientId"];
        private static string appKey = ConfigurationManager.AppSettings["ida:AppKey"];

        static string authority = String.Format(CultureInfo.InvariantCulture, aadInstance, tenant);

        //
        // To authenticate to the To Do list service, the client needs to know the service's App ID URI.
        // To contact the To Do list service we need it's URL as well.
        //
        private static string todoListResourceId = ConfigurationManager.AppSettings["todo:TodoListResourceId"];
        private static string todoListBaseAddress = ConfigurationManager.AppSettings["todo:TodoListBaseAddress"];

        private static HttpClient httpClient = new HttpClient();
        private static AuthenticationContext authContext = new AuthenticationContext(authority);
        private static ClientCredential clientCredential = new ClientCredential(clientId, appKey);

        private const string TenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

        //
        // GET: /TodoList/
        public async Task<ActionResult> Index()
        {
            List<TodoItem> itemList = new List<TodoItem>();

            //
            // Get a token to call the To Do list.
            // This sample uses the web app's identity to call the service, not the user's identity.
            // The web app has special privilege in the service to view and update any user's To Do list.
            // Since the web app is trusted to sign the user in, it is a trusted sub-system.
            //

            //
            // Get an access token from Azure AD using client credentials.
            // If the attempt to get a token fails because the server is unavailable, retry twice after 3 seconds each.
            //
            AuthenticationResult result = null;
            int retryCount = 0;
            bool retry = false;

            do
            {
                retry = false;
                try
                {
                    // ADAL includes an in memory cache, so this call will only send a message to the server if the cached token is expired.
                    result = await authContext.AcquireTokenAsync(todoListResourceId, clientCredential);
                }
                catch (AdalException ex)
                {
                    if (ex.ErrorCode == "temporarily_unavailable")
                    {
                        retry = true;
                        retryCount++;
                        Thread.Sleep(3000);
                    }
                }

            } while ((retry == true) && (retryCount < 3));

            if (result == null)
            {
                // Handle unexpected errors.
                ViewBag.ErrorMessage = "UnexpectedError";
                TodoItem newItem = new TodoItem();
                newItem.Title = "(No items in list)";
                itemList.Add(newItem);
                return View(itemList);
            }

            // Retrieve the user's Object Identifier claim, which is used as the key to the To Do list.
            string ownerId = ClaimsPrincipal.Current.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value;

            //
            // Retrieve the user's To Do List.
            //
            HttpClient client = new HttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, todoListBaseAddress + "/api/todolist?ownerid=" + ownerId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
            HttpResponseMessage response = await client.SendAsync(request);

            //
            // Return the To Do List in the view.
            //
            if (response.IsSuccessStatusCode)
            {
                List<Dictionary<String, String>> responseElements = new List<Dictionary<String, String>>();
                JsonSerializerSettings settings = new JsonSerializerSettings();
                String responseString = await response.Content.ReadAsStringAsync();
                responseElements = JsonConvert.DeserializeObject<List<Dictionary<String, String>>>(responseString, settings);
                foreach (Dictionary<String, String> responseElement in responseElements)
                {
                    TodoItem newItem = new TodoItem();
                    newItem.Title = responseElement["Title"];
                    newItem.Owner = responseElement["Owner"];
                    itemList.Add(newItem);
                }

                return View(itemList);
            }
            else
            {
                // If the response is Unauthorized, drop the token cache to force getting a new token on the next try.
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    authContext.TokenCache.Clear();
                }

                // Handle unexpected errors.
                ViewBag.ErrorMessage = "UnexpectedError";
                TodoItem newItem = new TodoItem();
                newItem.Title = "(No items in list)";
                itemList.Add(newItem);
                return View(itemList);
            }

        }

        [HttpPost]
        public async Task<ActionResult> Index(string item)
        {
            if (ModelState.IsValid)
            {
                List<TodoItem> itemList = new List<TodoItem>();

                //
                // Get an access token from Azure AD using client credentials.
                // If the attempt to get a token fails because the server is unavailable, retry twice after 3 seconds each.
                //
                AuthenticationResult result = null;
                int retryCount = 0;
                bool retry = false;

                do
                {
                    retry = false;
                    try
                    {
                        // ADAL includes an in memory cache, so this call will only send a message to the server if the cached token is expired.
                        result = await authContext.AcquireTokenAsync(todoListResourceId, clientCredential);
                    }
                    catch (AdalException ex)
                    {
                        if (ex.ErrorCode == "temporarily_unavailable")
                        {
                            retry = true;
                            retryCount++;
                            Thread.Sleep(3000);
                        }
                    }

                } while ((retry == true) && (retryCount < 3));

                if (result == null)
                {
                    // Handle unexpected errors.
                    ViewBag.ErrorMessage = "UnexpectedError";
                    TodoItem newItem = new TodoItem();
                    newItem.Title = "(No items in list)";
                    itemList.Add(newItem);
                    return View(itemList);
                }

                // Retrieve the user's Object Identifier claim, which is used as the key to the To Do list.
                string ownerId = ClaimsPrincipal.Current.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value;

                // Forms encode todo item, to POST to the todo list web api.
                HttpContent content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Title", item), new KeyValuePair<string, string>("Owner", ownerId) });

                //
                // Add the item to user's To Do List.
                //
                HttpClient client = new HttpClient();
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, todoListBaseAddress + "/api/todolist");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
                request.Content = content;
                HttpResponseMessage response = await client.SendAsync(request);

                //
                // Return the To Do List in the view.
                //
                if (response.IsSuccessStatusCode)
                {
                    return RedirectToAction("Index");
                }
                else
                {
                    // If the response is Unauthorized, drop the token cache to force getting a new token on the next try.
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        authContext.TokenCache.Clear();
                    }

                    // Handle unexpected errors.
                    ViewBag.ErrorMessage = "UnexpectedError";
                    TodoItem newItem = new TodoItem();
                    newItem.Title = "(No items in list)";
                    itemList.Add(newItem);
                    return View(itemList);
                }

            }

            return View("Error");
        }
    }
}