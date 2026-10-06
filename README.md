<p align="center">
  <img src="client/public/favicon.svg" alt="Cleared" width="72" height="72">
</p>

<h1 align="center">Cleared</h1>

<p align="center">
  <strong>Invoicing and VAT for South African small businesses, done right the first time.</strong>
</p>

Cleared helps small businesses in South Africa invoice correctly, keep track of what their customers owe and stay on the right side of VAT, without having to be tax experts. It is in active development and not yet open to the public.

## Why Cleared

Most small businesses still invoice from a spreadsheet or a word processor. That works until it does not: a document that says "Tax Invoice" when the business is not VAT registered, a required detail left off, a number that was skipped, a payment nobody can find. Each one is a small slip with a real cost at VAT time.

Cleared knows the rules and applies them as the invoice is written. It keeps an honest record of what was billed, what was paid and what is still owed, and it is built around how South African customers really pay: mostly by bank transfer.

## What you can do today

- **Invoice with confidence.** Add your customers, build an invoice line by line and issue it when it is ready. Invoice numbers run in sequence with no gaps.
- **Get VAT right.** Standard, zero-rated and exempt lines are handled for you. VAT rates carry the dates they apply from, so a rate change never rewrites an old invoice.
- **Issue the right document.** A business that is not VAT registered never produces a "Tax Invoice". A registered one is checked for the key details the VAT Act asks for before an invoice goes out.
- **Track payments.** Record bank transfers as they arrive, part-payments included, and always see what is still owed.
- **Look professional.** Every invoice downloads as a clean PDF you can share with your customer.
- **Keep a trustworthy record.** Issued invoices are locked, and every key action is written to an audit trail.
- **Your data stays yours.** Many businesses share one platform, and each one sees only its own records.

## Where it is heading

- Quotes that turn into invoices
- A dashboard of what is outstanding, paid and overdue
- A VAT summary that lines up with the VAT201 return
- Credit notes for correcting issued invoices
- Invoices sent by email
- Online payments through a South African payment gateway
- Matching bank statements to invoices
- Team members with their own roles
- Electronic tax invoicing, once the rules are confirmed

## Built with

| Area | Technology |
|---|---|
| Backend | ASP.NET Core 10 and C# |
| Database | PostgreSQL 17, through Entity Framework Core |
| Web app | Angular 22, TypeScript and Tailwind CSS |
| Sign-in | ASP.NET Core Identity with JSON Web Tokens |
| Invoice PDFs | QuestPDF |
| Quality | xUnit and Testcontainers on the server, Vitest on the web app, ESLint, and GitHub Actions for continuous integration |

Hosting is planned on AWS, in the Cape Town region.

## Status

Cleared is in early development and is not open for sign-ups yet. The core flow already works from start to finish: register a business, add a customer, issue an invoice, record the payment and download the PDF.

Working on the code? The build and run notes are in [docs/development.md](docs/development.md).
