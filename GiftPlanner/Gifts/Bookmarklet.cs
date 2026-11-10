using System.Text.RegularExpressions;

namespace GiftPlanner.Gifts;

/// <summary>
/// The "Add to Gift Planner" bookmark. It runs on the product page you're already viewing in your own browser,
/// reads the name / price / picture from that page, and opens Gift Planner's /add page with them filled in.
/// </summary>
public static partial class Bookmarklet
{
    // Readable source; whitespace is collapsed when building the bookmark.
    private const string Source = """
        (()=>{
        const B='__BASE__';
        const q=s=>{try{return document.querySelector(s)}catch(e){return null}};
        const m=n=>{const e=q('meta[property="'+n+'"],meta[name="'+n+'"],meta[itemprop="'+n+'"]');return e&&e.content};
        const txt=s=>{const e=q(s);return e&&e.textContent.trim()};
        let t,p,i;
        const find=x=>{if(!x||typeof x!=='object')return;if(Array.isArray(x)){for(const y of x){const r=find(y);if(r)return r}return}
          if(/Product/.test(String(x['@type'])))return x;return find(x['@graph'])||find(x.mainEntity)};
        for(const s of document.querySelectorAll('script[type="application/ld+json"]')){
          try{const r=find(JSON.parse(s.textContent));if(!r)continue;
            const v=r.hasVariant?[].concat(r.hasVariant)[0]:null;
            const o=[].concat(r.offers||(v&&v.offers)||[])[0]||{};
            const sp=[].concat(o.priceSpecification||[])[0]||{};
            t=t||r.name;p=p||o.price||o.lowPrice||sp.price;
            let im=[].concat(r.image||(v&&v.image)||[])[0];i=i||(im&&(im.url||im))}catch(e){}}
        p=p||m('product:price:amount')||m('og:price:amount')||m('price')
          ||txt('[data-test="product-price"]')||txt('#corePrice_feature_div .a-offscreen')||txt('.priceToPay .a-offscreen')
          ||txt('[data-testid="customer-price"] span')||txt('.priceView-customer-price span')||txt('[itemprop="price"]');
        const h1=()=>{for(const e of document.querySelectorAll('h1')){if(e.closest('aside,dialog,nav,header,[role=dialog],[aria-hidden=true],[id*=chat i],[class*=chat i],[id*=rufus i],[class*=rufus i]'))continue;
          const r=e.getBoundingClientRect(),s=e.textContent.trim();if(s&&r.width>0&&r.height>0&&!/chat|history|assistant/i.test(s))return s}};
        t=t||txt('#productTitle')||txt('#title')||m('og:title')||txt('[data-test="product-title"]')||txt('[itemprop="name"]')||h1()||document.title;
        i=i||m('og:image')||(q('#landingImage')||{}).src;
        const pm=String(p||'').replace(/,/g,'').match(/\d+(\.\d+)?/);
        const P=new URLSearchParams({u:location.href.split('#')[0],t:t||'',p:pm?pm[0]:'',i:typeof i==='string'?i:''});
        const w=window.open(B+'add?'+P,'giftplanner','width=560,height=780');
        if(!w)location.href=B+'add?'+P;
        })()
        """;

    /// <summary>The javascript: URL to put in a bookmark, pointed at this Gift Planner.</summary>
    public static string Build(string baseUrl)
    {
        var code = Whitespace().Replace(Source.Replace("__BASE__", baseUrl.TrimEnd('/') + "/"), " ").Trim();
        // Escape only what breaks a URL; keep it readable if someone inspects the bookmark.
        return "javascript:" + code.Replace("%", "%25").Replace("\"", "%22").Replace("#", "%23");
    }

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex Whitespace();
}
