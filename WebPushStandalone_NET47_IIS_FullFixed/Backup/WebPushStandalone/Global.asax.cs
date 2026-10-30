using System.Web;
using System.Web.Http;
using WebPushStandalone.App_Start;
namespace WebPushStandalone
{
    public class WebApiApplication : HttpApplication
    {
        protected void Application_Start() { GlobalConfiguration.Configure(WebApiConfig.Register); }
    }
}
