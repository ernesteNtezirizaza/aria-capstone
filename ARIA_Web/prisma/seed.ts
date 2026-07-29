/* One-time script to create the first Admin account, since Admins create
   every other account but none exist yet on a fresh database. Run once:
     npm run db:seed
   (safe to re-run -- it no-ops if an active admin already exists). */
import { config } from 'dotenv';
config({ path: '.env' });
config({ path: '.env.local', override: true });

import { PrismaClient } from '@prisma/client';
import { Pool } from 'pg';
import { PrismaPg } from '@prisma/adapter-pg';
import bcrypt from 'bcryptjs';

async function main() {
  const email = process.env.FIRST_ADMIN_EMAIL;
  const password = process.env.FIRST_ADMIN_PASSWORD;
  const name = process.env.FIRST_ADMIN_NAME || 'Admin';

  if (!email || !password) {
    console.error(
      'Set FIRST_ADMIN_EMAIL and FIRST_ADMIN_PASSWORD (in .env.local) before running the seed script.'
    );
    process.exit(1);
  }
  if (password.length < 8) {
    console.error('FIRST_ADMIN_PASSWORD must be at least 8 characters.');
    process.exit(1);
  }

  const connectionString = process.env.DATABASE_URL!;
  const url = new URL(connectionString);
  if (url.searchParams.get('sslmode') === 'require') {
    url.searchParams.set('uselibpqcompat', '1');
  }
  const pool = new Pool({ connectionString: url.toString() });
  const adapter = new PrismaPg(pool);
  const prisma = new PrismaClient({ adapter });

  try {
    const existingAdmin = await prisma.user.findFirst({ where: { role: 'ADMIN', status: 'ACTIVE' } });
    if (existingAdmin) {
      console.log(`An active admin already exists (${existingAdmin.email}). Nothing to do.`);
      return;
    }

    const normalizedEmail = email.toLowerCase();
    const existingByEmail = await prisma.user.findUnique({ where: { email: normalizedEmail } });
    if (existingByEmail) {
      console.log(
        `A user with email ${normalizedEmail} already exists (role=${existingByEmail.role}, status=${existingByEmail.status}). Skipping.`
      );
      return;
    }

    const passwordHash = await bcrypt.hash(password, 10);
    const admin = await prisma.user.create({
      data: { name, email: normalizedEmail, passwordHash, role: 'ADMIN', status: 'ACTIVE' },
    });

    console.log(`Created first admin: ${admin.email} (id ${admin.id}). You can now log in at /login.`);
  } finally {
    await prisma.$disconnect();
    await pool.end();
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
