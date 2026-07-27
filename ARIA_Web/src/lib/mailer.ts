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

function otpEmailHtml(name: string, otp: string, purpose: OtpPurpose) {
  const isVerify = purpose === 'VERIFY_EMAIL';
  const intro = isVerify
    ? 'An ARIA administrator created an account for you.'
    : 'We received a request to reset your ARIA account password.';
  const action = isVerify ? 'activate your account' : 'reset your password';

  return `
    <div style="font-family: -apple-system, Segoe UI, Roboto, sans-serif; max-width: 480px; margin: 0 auto; color: #0f172a;">
      <h2 style="color: #059669; margin-bottom: 4px;">ARIA</h2>
      <p>Hi ${name},</p>
      <p>${intro} Use the code below to ${action}:</p>
      <p style="font-size: 32px; font-weight: bold; letter-spacing: 8px; background: #f1f5f9; padding: 16px; text-align: center; border-radius: 8px; color: #0f172a;">${otp}</p>
      <p style="color: #64748b; font-size: 13px;">This code expires in 10 minutes. If you didn't request this, you can safely ignore this email.</p>
    </div>
  `;
}

export async function sendOtpEmail(to: string, name: string, otp: string, purpose: OtpPurpose) {
  const transporter = getTransporter();
  const subject = purpose === 'VERIFY_EMAIL' ? 'Verify your ARIA account' : 'Reset your ARIA password';

  await transporter.sendMail({
    from: process.env.SMTP_FROM || process.env.SMTP_USER,
    to,
    subject,
    html: otpEmailHtml(name, otp, purpose),
  });
}
