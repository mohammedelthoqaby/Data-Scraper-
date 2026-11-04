# LazyBean CI v2 — Workflow Design

Nov 3, 2026 · @Mohamed Elthoqaby

## رأيي في المقارنة اللي في الصور

الخلاصة صحيحة في اتجاهها (ابدأ بـ Playwright وAngleSharp)، لكن فيها معلومة غلط ومعيار غلط، والأهم إنها بتتجاهل أكبر اكتشاف عندنا: موقعين من الخمسة عندهم JSON جاهز ومش محتاجين متصفح أصلًا.

| النقطة في الصور | رأيي | ليه |
| --- | --- | --- |
| ابدأ بـ Playwright for .NET | موافق | مشروع رسمي من Microsoft ومناسب للمواقع اللي محتاجة JavaScript. |
| Crawlee for .NET كخيار للزحف الضخم | غلط | Crawlee متاح رسميًا لـ Node.js/TypeScript وPython فقط؛ مفيش نسخة رسمية لـ .NET ([Crawlee على GitHub](https://github.com/apify/crawlee)). |
| PuppeteerSharp + Stealth لتخطي الحماية | مش هنبني عليه | المكتبة نفسها نشطة وبتدعم .NET 10، لكن وجود مكتبتين متصفح يكرر الصيانة. وبناء النظام على تخطي أنظمة الحماية هش وبيعرض الشركة لمخاطر قانونية. |
| "Playwright قوي جدًا في تخطي الحجب" | مبالغة | المتصفح الـ headless ممكن اكتشافه بسهولة. قوته في تشغيل JavaScript، مش في التخفي. |
| مقاومة الحجب كعمود لـ AngleSharp وHtmlAgilityPack | معيار غلط | دول Parsers بيحللوا HTML بعد ما يوصل؛ مالهمش علاقة بالحجب. اللي بيتعامل مع الطلبات هو HttpClient. |
| AngleSharp أو HtmlAgilityPack | AngleSharp | الاتنين كويسين؛ نختار واحد بس. AngleSharp بيدعم CSS Selectors وده أسهل في كتابة الـ Adapters. |

النتيجة الـ 429 اللي ظهرت معناها إن السيرفر بيقول "بطّأ"، مش "اتخفى". الحل الصح هو Rate Limit واحترام Retry-After، مش Stealth.

بخصوص Astro: ده تغيير عن قرارك السابق بالاحتفاظ بنفس الـ Frontend، وهو تغيير معقول. لكن الداشبورد تطبيق تفاعلي ورا تسجيل دخول، فأغلب الصفحات هتكون React islands. أرشح Astro بـ `output: 'static'` (من غير Node server)، يتبني ملفات ثابتة ويتخدم من Nginx، وكل البيانات من ASP.NET API. كده مفيش Runtime تالت تديره على السيرفر.

## الـ Stack النهائي

مكتبة واحدة لكل مسؤولية، وكلها مفتوحة المصدر وتحت سيطرتك؛ مفيش Framework زحف خارجي في قلب النظام.

| الطبقة | الاختيار | الدور |
| --- | --- | --- |
| Frontend | Astro (static) + React islands + TypeScript | الداشبورد والصفحات |
| Data fetching في الواجهة | TanStack Query | Cache وRefetch وحالة التحميل |
| Charts / Tables | Recharts أو ECharts + TanStack Table | مقارنات الأسعار والتاريخ |
| API | ASP.NET Core (.NET 10 LTS) | REST + Auth + تشغيل الـ Runs |
| Worker | .NET Worker Service | تشغيل الـ Scraping بعيد عن الـ API |
| Scheduling | Quartz.NET (PostgreSQL job store) | التحديث اليومي والتشغيل اليدوي |
| Queue داخلية | System.Threading.Channels | طابور الروابط والتوازي |
| HTTP | HttpClientFactory + Microsoft.Extensions.Http.Resilience | Retry وBackoff وCircuit breaker |
| Parsing | AngleSharp + System.Text.Json | HTML وJSON-LD وJSON APIs |
| Browser (عند الحاجة فقط) | Microsoft.Playwright | صفحات JavaScript |
| Database | PostgreSQL + EF Core | المنتجات والملاحظات والتاريخ |
| Realtime (اختياري) | Server-Sent Events | تقدم الـ Run لحظيًا في الواجهة |
| Logs / Metrics | Serilog + OpenTelemetry | مراقبة صحة كل Adapter |
| Deployment | Docker Compose + Nginx + HTTPS | سيرفر واحد |

## Architecture overview

&#91;embedded content: architecture · 4 runtime services\]

الـ API والـ Worker منفصلين: الواجهة بتطلب Run، الـ API يسجله، والـ Worker يلتقطه وينفذه ويكتب النتائج في نفس الـ Database.

## Collection workflow

&#91;embedded content: collection run · 11 steps, 3 decisions\]

المتصفح بيشتغل بس لما HTTP ميجيبش بيانات المنتج، والداتا المشكوك فيها بتروح للمراجعة، ومفيش منتج يتعلم removed إلا بعد زحف كامل.

## الـ 5 Competitor Adapters

كل Adapter بيختار أرخص مصدر يشتغل: API أولًا، بعدين HTML/JSON-LD، والمتصفح آخر حل. كلهم بيرجعوا نفس الـ `RawProduct`.

| المنافس | المنصة | المصدر الأساسي | البديل | الحالة |
| --- | --- | --- | --- | --- |
| Coffee Zone Misr | Shopify | `/products.json?page=N&limit=250` | `/products/{handle}.js` + Sitemap | مؤكد بالاختبار |
| Fengany | Shopify | `/products.json?page=N&limit=250` | `/products/{handle}.js` + Sitemap | مؤكد بالاختبار (رجّع 429 مرة؛ يشتغل بفاصل 3 ثواني) |
| Coffee & Cream | Shopify | `/products.json?page=N&limit=250` | `/products/{handle}.js` + Sitemap | مؤكد بالاختبار |
| CAFELAX | Shopify | `/products.json?page=N&limit=250` | `/products/{handle}.js` + Sitemap | مؤكد بالاختبار |
| Strong Coffee | WooCommerce | `/wp-json/wc/store/v1/products?page=N&per_page=100` | Sitemap | مؤكد بالاختبار (452 منتج) |

الخمسة بيرجعوا JSON مباشرة، فمفيش أي منافس محتاج Playwright حاليًا. ملاحظات من الاختبار: أسعار Woo بالـ minor units (تتقسم على 100)، و`grams` في Shopify بيرجع 0 فالوزن يتستخرج من الاسم، والأسماء فيها HTML entities وعربي مخلوط، و`updated_at` في Shopify مش موثوق لاكتشاف التغيير.

### الـ Contract المشترك

```csharp
public interface ICompetitorAdapter
{
    string CompetitorKey { get; }
    AdapterPolicy Policy { get; }          // rate limit, concurrency, robots, browser allowed

    IAsyncEnumerable<CrawlTarget> DiscoverAsync(AdapterContext ctx, CancellationToken ct);
    Task<ExtractResult> ExtractAsync(FetchedResource res, AdapterContext ctx, CancellationToken ct);
}

public sealed record RawProduct(
    string CompetitorKey, string ExternalId, string Url, string Title,
    IReadOnlyList<RawVariant> Variants, string? ImageUrl, string? Category,
    IReadOnlyList<RawOffer> Offers, string SourceKind /* api | jsonld | dom | browser */);

public sealed record RawVariant(
    string ExternalId, string? Title, decimal Price, decimal? CompareAtPrice,
    string Currency, bool? InStock, string? Sku, string? WeightText);
```

### Base classes عشان منكررش الكود

- `ShopifyAdapterBase`: Pagination على `products.json` لحد ما ترجع صفحة فاضية، وتحويل الـ variants و`compare_at_price`.
- `WooStoreApiAdapterBase`: Pagination بالـ headers `X-WP-TotalPages`، والأسعار بتيجي بـ `currency_minor_unit` فلازم نقسم عليه.
- `HtmlAdapterBase`: Discovery من Sitemap وصفحات الأقسام، واستخراج JSON-LD أولًا ثم Selectors خاصة بالموقع.

كل منافس = كلاس صغير يورث من الـ base المناسب ويحدد بس اللي مختلف: الروابط، الـ Selectors، قواعد العروض، والـ Policy.

## Data model

المبدأ الأساسي: المنتج له هوية ثابتة، والسعر عبارة عن Observation جديدة في كل Run. مفيش UPDATE على سعر قديم.

| الجدول | أهم الأعمدة | الغرض |
| --- | --- | --- |
| competitors | id, key, name, base\_url, platform, is\_reference, enabled | المنافسين + Lazy Bean كمرجع |
| adapter\_configs | competitor\_id, policy\_json, version | Rate limit والإعدادات بدون Deploy |
| collection\_runs | id, trigger (manual/scheduled), status, started\_at, finished\_at | الـ Snapshot |
| competitor\_runs | run\_id, competitor\_id, status, pages, products, errors, coverage | نتيجة كل منافس داخل الـ Run |
| crawled\_resources | id, competitor\_run\_id, url, kind, http\_status, fetched\_via, content\_hash | كل صفحة/طلب API |
| evidence | content\_hash, storage\_path, content\_type, size | النسخة الخام (على Disk مضغوطة) |
| products | id, competitor\_id, external\_id, canonical\_url, title, normalized\_title, weight\_g, roast, origin, first\_seen, last\_seen, status | هوية المنتج |
| variants | id, product\_id, external\_id, title, weight\_g, sku | الأوزان والأحجام |
| price\_observations | id, variant\_id, run\_id, price, compare\_at\_price, currency, in\_stock, observed\_at, resource\_id | تاريخ السعر |
| offers | id, competitor\_id, run\_id, type (discount/bundle/code/gift/shipping), details\_json, product\_id? | العروض |
| product\_matches | competitor\_variant\_id, reference\_variant\_id, score, method, status (auto/approved/rejected) | الربط مع Lazy Bean |
| change\_events | id, run\_id, variant\_id, type, old\_value, new\_value | تغير سعر/مخزون/منتج جديد |
| review\_queue | id, kind, payload\_json, reason, status | البيانات المشكوك فيها والمطابقات |

منتج يتعلم عليه `removed` بس لو الـ `competitor_run` بتاعه خلص بـ `coverage = complete`. لو الـ Run فشل في النص، مفيش أي حذف.

## API contract

بما إن الواجهة هتتكتب من جديد، نصمم الـ API نظيف من الأول، ونولّد TypeScript types من OpenAPI عشان الـ Frontend والـ Backend ميختلفوش.

| Method | Route | الوظيفة |
| --- | --- | --- |
| POST | /api/auth/login · POST /api/auth/logout · GET /api/auth/me | الدخول (Cookie session) |
| GET | /api/competitors | المنافسين وحالة آخر Run لكل واحد |
| PATCH | /api/competitors/{id} | تفعيل/إيقاف وتعديل الـ Policy |
| POST | /api/runs | تشغيل Collection يدوي (كل المنافسين أو بعضهم) |
| GET | /api/runs · GET /api/runs/{id} | قائمة الـ Snapshots وتفاصيلها |
| GET | /api/runs/{id}/events | SSE لتقدم الـ Run لحظيًا |
| POST | /api/runs/{id}/cancel | إلغاء |
| GET | /api/runs/{id}/price-matrix | مقارنة Lazy Bean مع المنافسين |
| GET | /api/runs/{id}/changes | التغيرات مقارنة بالـ Run السابق |
| GET | /api/products?competitor=&q=&page= | المنتجات |
| GET | /api/products/{id}/history | تاريخ الأسعار لكل Variant |
| GET | /api/offers?run= | العروض والحملات |
| GET | /api/matches · PATCH /api/matches/{id} | مراجعة المطابقات (approve/reject) |
| GET | /api/reviews · PATCH /api/reviews/{id} | الـ Review queue |
| GET | /api/evidence/{hash} | الدليل الخام |
| GET | /api/exports/runs/{id}.csv | تصدير |
| GET | /api/health | صحة النظام |

الـ API عمره ما يشغل Scraping بنفسه: `POST /api/runs` بيسجل الـ Run كـ `queued` بس، والـ Worker هو اللي بيلتقطه.

## Frontend: Astro + React

Astro بيدي الـ Layout والـ Routing وملفات ثابتة سريعة؛ React بيدي كل الأجزاء التفاعلية كـ islands. الـ Build النهائي مجلد `dist/` يتخدم من Nginx.

```text
web/
├── astro.config.mjs          # output: 'static', integrations: [react()]
├── src/
│   ├── layouts/
│   │   └── AppLayout.astro    # sidebar + header + auth guard island
│   ├── pages/
│   │   ├── index.astro               # Overview
│   │   ├── login.astro
│   │   ├── price-comparisons.astro
│   │   ├── products/index.astro
│   │   ├── products/[id].astro       # client-side routed detail
│   │   ├── offers.astro
│   │   ├── changes.astro
│   │   ├── reviews.astro
│   │   ├── runs/index.astro
│   │   └── settings/competitors.astro
│   ├── islands/                # React components mounted with client:load
│   │   ├── PriceMatrix.tsx
│   │   ├── PriceHistoryChart.tsx
│   │   ├── RunLauncher.tsx     # زر Start Collection + تقدم SSE
│   │   ├── ChangesFeed.tsx
│   │   ├── MatchReview.tsx
│   │   └── CompetitorHealth.tsx
│   ├── lib/
│   │   ├── api/                # client مولّد من OpenAPI
│   │   ├── query.ts            # TanStack QueryClient
│   │   └── format.ts           # EGP، التواريخ بتوقيت القاهرة
│   └── styles/
└── package.json
```

نقاط مهمة:

- الصفحات الديناميكية زي `products/[id]` في وضع static تحتاج `getStaticPaths`؛ الأسهل نخلي صفحة واحدة `products/detail.astro?id=` أو routing داخل island.
- الـ API على نفس الـ Domain تحت `/api`، فمفيش CORS، والـ Auth بـ HttpOnly cookie.
- الواجهة ممكن تتعرض على Vercel كملفات ثابتة لو حبيت، بس ساعتها لازم CORS وCookie cross-site؛ نفس الـ Domain أبسط.

## Scheduling، الاعتمادية، والمراقبة

النظام لازم يكون مؤدب مع المواقع وصريح لما يفشل؛ ده أهم من السرعة.

- **الجدولة:** Quartz job يومي الساعة 7 صباحًا بتوقيت القاهرة (`Africa/Cairo`) + تشغيل يدوي من الداشبورد. منع تشغيل Run تاني لو فيه واحد شغال.
- **Rate limit لكل Domain:** طلب واحد كل 2 إلى 3 ثواني افتراضيًا، وتوازي 1 أو 2 لكل منافس. المنافسين الخمسة يشتغلوا بالتوازي مع بعض.
- **429 و503:** نحترم `Retry-After`، ونعمل exponential backoff بحد أقصى 3 محاولات، وبعدها Circuit breaker يوقف المنافس ده في الـ Run ده.
- **robots.txt:** يتقرا مرة في كل Run وأي مسار ممنوع يتسجل كـ skipped.
- **User-Agent:** ثابت ويعرّف النظام. مفيش Stealth ولا تدوير Proxies ولا حل CAPTCHA.
- **Playwright:** متصفح واحد مشترك في الـ Worker، Context لكل منافس، ويتقفل بعد الـ Run. يشتغل بس لو الـ Adapter طلبه.
- **Validation:** سعر صفر أو سالب، أو تغير أكبر من 60% عن آخر Observation، أو عملة غير EGP، أو `compare_at_price` أقل من السعر الحالي → يروح الـ Review queue بدل الداتا.
- **كشف كسر الـ Adapter:** لو عدد المنتجات نزل أكثر من 30% عن متوسط آخر 3 Runs، الـ competitor\_run يتعلم `degraded` ومفيش منتجات تتعلم removed.
- **المراقبة:** Serilog structured logs، وMetrics لكل منافس (مدة، صفحات، منتجات، أخطاء، نسبة browser)، وتنبيه بالإيميل أو Slack لما منافس يفشل أو يبقى degraded.

النسب دي (2-3 ثواني، 60%، 30%) بدايات معقولة نظبطها بعد أول أسبوعين من البيانات.

## Deployment

سيرفر VPS واحد (Ubuntu، 4 GB RAM كحد أدنى بسبب Chromium) بيشغل كل حاجة عن طريق Docker Compose.

```yaml
services:
  nginx:        # يخدم web/dist ويعمل reverse proxy لـ /api + HTTPS (Let's Encrypt)
    image: nginx:stable
    ports: ["80:80", "443:443"]
  api:          # ASP.NET Core
    build: ./src/LazyBean.Api
    environment: [ConnectionStrings__Db, Auth__CookieSecret]
  worker:       # Quartz + Scraping + Playwright
    build: ./src/LazyBean.Worker   # FROM mcr.microsoft.com/playwright/dotnet
    volumes: [evidence:/data/evidence]
  postgres:
    image: postgres:17
    volumes: [pgdata:/var/lib/postgresql/data]
  backup:       # pg_dump يومي + ضغط الـ evidence القديمة
volumes: { pgdata: {}, evidence: {} }
```

- Postgres مش متعرض على الإنترنت؛ بس على الشبكة الداخلية.
- الأسرار في `.env` على السيرفر، مش في الـ Repo.
- CI على GitHub Actions: build + tests + بناء الـ images، والـ Deploy بـ `docker compose pull && up -d`.
- Evidence أقدم من 90 يوم تتحذف، والـ Observations تفضل دايمًا.

## خطة التنفيذ

نبدأ بالموقعين المؤكدين لأنهم أقل مخاطرة، ومفيش مرحلة تبدأ قبل ما اللي قبلها يعدي شرطه.

1. **فحص باقي المواقع:** نكمل الـ Recon لـ Coffee & Cream وCAFELAX وFengany. الشرط: المنصة ومصدر البيانات معروفين للخمسة.
2. **الأساس:** Solution بـ .NET 10، EF Core migrations، API بالـ Auth، Worker بـ Quartz، Docker Compose محلي. الشرط: Run فاضي يتسجل ويخلص.
3. **Scraping Core:** Fetcher + Rate limiter + Resilience + Evidence store + Pipeline التطبيع والـ Validation. الشرط: Tests على HTML وJSON محفوظين.
4. **أول 2 Adapters:** Coffee Zone (Shopify) وStrong Coffee (Woo API). الشرط: الكتالوج كله بالـ Variants والأسعار في 3 Runs متتالية.
5. **باقي الـ Adapters:** حسب نتيجة الفحص، مع fixtures لكل موقع.
6. **Intelligence:** المطابقة مع كتالوج Lazy Bean، الـ Change events، والعروض.
7. **Frontend:** Astro + React على الـ API الجاهزة، بالترتيب: Runs → Price matrix → Products/History → Changes → Reviews.
8. **Production:** VPS + HTTPS + Backups + Alerts، وأسبوع تشغيل تجريبي قبل الاعتماد على الأرقام.
