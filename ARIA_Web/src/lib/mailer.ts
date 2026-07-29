import nodemailer from 'nodemailer';
import type { OtpPurpose } from '@prisma/client';

function getTransporter() {
  const host = process.env.SMTP_HOST;
  const port = Number(process.env.SMTP_PORT || 587);
  const user = process.env.SMTP_USER;
  const pass = process.env.SMTP_PASS;
  if (!host || !user || !pass) {
    throw new Error('SMTP is not configured (missing SMTP_HOST/SMTP_USER/SMTP_PASS).');
  }
  return nodemailer.createTransport({
    host,
    port,
    secure: port === 465,
    auth: { user, pass },
  });
}

/* Prefers an explicit public URL, falls back to Vercel's own env vars (set
   automatically on every deploy), then localhost for local dev -- so the
   email's link always points at wherever this is actually running rather
   than a hardcoded domain. */
function getAppUrl(): string {
  if (process.env.NEXT_PUBLIC_APP_URL) return process.env.NEXT_PUBLIC_APP_URL;
  if (process.env.VERCEL_PROJECT_PRODUCTION_URL) return `https://${process.env.VERCEL_PROJECT_PRODUCTION_URL}`;
  if (process.env.VERCEL_URL) return `https://${process.env.VERCEL_URL}`;
  return 'http://localhost:3000';
}

function otpEmailHtml(name: string, otp: string, purpose: OtpPurpose, actionUrl: string) {
  const isVerify = purpose === 'VERIFY_EMAIL';
  const intro = isVerify
    ? 'An ARIA administrator created an account for you.'
    : 'We received a request to reset your ARIA account password.';
  const actionLabel = isVerify ? 'Activate account & set password' : 'Reset your password';
  const heading = isVerify ? 'Activate your account' : 'Reset your password';

  return `
  <div style="background:#f1f5f9; padding:32px 16px; font-family:-apple-system,'Segoe UI',Roboto,sans-serif;">
    <div style="max-width:480px; margin:0 auto; background:#ffffff; border-radius:20px; overflow:hidden; border:1px solid #e2e8f0;">
      <div style="background:linear-gradient(135deg,#059669,#0891b2); padding:28px 32px;">
        <span style="color:#ffffff; font-size:22px; font-weight:800; letter-spacing:0.5px;">ARIA</span>
      </div>
      <div style="padding:32px;">
        <h1 style="margin:0 0 16px; font-size:20px; color:#0f172a;">${heading}</h1>
        <p style="margin:0 0 20px; color:#334155; font-size:14px; line-height:1.6;">
          Hi ${name},<br /><br />
          ${intro} Click the button below to continue -- your verification code is already filled in.
        </p>

        <div style="text-align:center; margin:28px 0;">
          <a href="${actionUrl}"
             style="display:inline-block; background:#059669; color:#ffffff; text-decoration:none; font-weight:600; font-size:14px; padding:14px 32px; border-radius:999px;">
            ${actionLabel}
          </a>
        </div>

        <p style="text-align:center; color:#94a3b8; font-size:12px; margin:0 0 4px;">Or enter this code manually:</p>
        <p style="font-size:28px; font-weight:800; letter-spacing:8px; background:#f1f5f9; padding:14px; text-align:center; border-radius:12px; color:#0f172a; margin:0 0 20px;">
          ${otp}
        </p>

        <p style="color:#94a3b8; font-size:12px; line-height:1.6; margin:0;">
          This code and link expire in 10 minutes. If you didn't request this, you can safely ignore this email --
          your account will stay unchanged.
        </p>
      </div>
    </div>
    <p style="text-align:center; color:#94a3b8; font-size:11px; margin-top:16px;">
      ARIA -- Adaptive Reforestation Intelligence Agent
    </p>
  </div>
  `;
}

export async function sendOtpEmail(to: string, name: string, otp: string, purpose: OtpPurpose) {
  const transporter = getTransporter();
  const subject = purpose === 'VERIFY_EMAIL' ? 'Verify your ARIA account' : 'Reset your ARIA password';

  const path = purpose === 'VERIFY_EMAIL' ? '/verify' : '/reset-password';
  const actionUrl = `${getAppUrl()}${path}?email=${encodeURIComponent(to)}&otp=${encodeURIComponent(otp)}`;

  await transporter.sendMail({
    from: process.env.SMTP_FROM || process.env.SMTP_USER,
    to,
    subject,
    html: otpEmailHtml(name, otp, purpose, actionUrl),
  });
}
