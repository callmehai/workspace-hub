// Template HTML email dựng sẵn — khung nhất quán (header + body + footer), inline style chuẩn email client.

import type { TranslationKey } from '../i18n/translations';

export interface EmailTemplate {
  id: string;
  labelKey: TranslationKey;
  subject?: string;
  html: string;
}

const FONT = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

/** Khung email chuẩn: header brand + body + footer. accent đổi màu header/nút theo loại email. */
const shell = (opts: { title: string; body: string; accent?: string; footer?: string }) => {
  const accent = opts.accent ?? '#2563eb';
  return `
<div style="margin:0;padding:24px 12px;background:#f3f4f6;font-family:${FONT};">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;">
    <tr><td align="center">
      <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#ffffff;border-radius:14px;overflow:hidden;border:1px solid #e8eaed;">
        <tr><td style="background:${accent};padding:20px 32px;">
          <table role="presentation" width="100%"><tr>
            <td style="color:#ffffff;font-size:18px;font-weight:700;letter-spacing:.2px;">${opts.title}</td>
            <td align="right" style="color:rgba(255,255,255,.9);font-size:13px;font-weight:600;">Workspace Hub</td>
          </tr></table>
        </td></tr>
        <tr><td style="padding:32px;">${opts.body}</td></tr>
        <tr><td style="padding:20px 32px;background:#f9fafb;border-top:1px solid #eef0f2;">
          <p style="margin:0;color:#9aa1ab;font-size:12px;line-height:1.7;">${opts.footer ?? 'Bạn nhận được email này từ Workspace Hub.'}<br/>© 2026 Workspace Hub · <a href="#" style="color:#9aa1ab;text-decoration:underline;">Huỷ nhận</a></p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</div>`.trim();
};

const btn = (label: string, accent = '#2563eb') =>
  `<a href="#" style="display:inline-block;background:${accent};color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:8px;font-size:14px;font-weight:600;">${label}</a>`;

const p = (text: string) =>
  `<p style="margin:0 0 14px;color:#374151;font-size:15px;line-height:1.7;">${text}</p>`;

export const EMAIL_TEMPLATES: EmailTemplate[] = [
  { id: 'blank', labelKey: 'template.blank', html: '' },

  {
    id: 'announcement',
    labelKey: 'template.announcement',
    subject: 'Thông báo quan trọng',
    html: shell({
      title: '📣 Thông báo',
      body: `
        <p style="margin:0 0 4px;color:#111827;font-size:18px;font-weight:700;">[Tiêu đề thông báo]</p>
        <p style="margin:0 0 20px;color:#6b7280;font-size:13px;">Cập nhật quan trọng dành cho bạn</p>
        ${p('Xin chào,')}
        ${p('Chúng tôi xin thông báo tới bạn nội dung dưới đây. Vui lòng thay bằng thông tin cụ thể và điều chỉnh cho phù hợp với mục đích của bạn.')}
        ${p('Nếu cần thêm chi tiết, đừng ngần ngại phản hồi email này — chúng tôi luôn sẵn sàng hỗ trợ.')}
        <div style="margin-top:24px;">${btn('Xem chi tiết')}</div>`,
    }),
  },

  {
    id: 'welcome',
    labelKey: 'template.welcome',
    subject: 'Chào mừng bạn!',
    html: shell({
      accent: '#4f46e5',
      title: '👋 Chào mừng',
      body: `
        <p style="margin:0 0 14px;color:#111827;font-size:22px;font-weight:700;">Chào mừng bạn đến với [Sản phẩm]!</p>
        ${p('Rất vui được đồng hành cùng bạn. Chỉ với vài bước dưới đây, bạn đã có thể bắt đầu:')}
        <table role="presentation" width="100%" style="margin:8px 0 24px;">
          ${[
            ['1', 'Hoàn thiện hồ sơ', 'Thêm thông tin để cá nhân hoá trải nghiệm.'],
            ['2', 'Kết nối dịch vụ', 'Liên kết tài khoản để đồng bộ dữ liệu.'],
            ['3', 'Khám phá tính năng', 'Bắt đầu với những công cụ hữu ích nhất.'],
          ].map(([n, t, d]) => `
          <tr>
            <td width="40" valign="top" style="padding:6px 0;">
              <div style="width:28px;height:28px;border-radius:50%;background:#eef2ff;color:#4f46e5;font-weight:700;font-size:13px;text-align:center;line-height:28px;">${n}</div>
            </td>
            <td style="padding:6px 0;">
              <p style="margin:0;color:#111827;font-size:15px;font-weight:600;">${t}</p>
              <p style="margin:2px 0 0;color:#6b7280;font-size:13px;line-height:1.6;">${d}</p>
            </td>
          </tr>`).join('')}
        </table>
        ${btn('Bắt đầu ngay', '#4f46e5')}`,
    }),
  },

  {
    id: 'meeting',
    labelKey: 'template.meeting',
    subject: 'Lời mời họp',
    html: shell({
      title: '📅 Lời mời họp',
      body: `
        ${p('Xin chào,')}
        ${p('Bạn được mời tham dự cuộc họp với các thông tin dưới đây. Rất mong bạn sắp xếp tham dự đúng giờ.')}
        <table role="presentation" width="100%" style="margin:4px 0 20px;background:#f9fafb;border:1px solid #eef0f2;border-radius:10px;">
          <tr><td style="padding:16px 18px;">
            ${[
              ['Chủ đề', '[Chủ đề cuộc họp]'],
              ['Thời gian', '[Thứ, dd/mm/yyyy · HH:mm]'],
              ['Địa điểm', '[Phòng họp / Link online]'],
              ['Thành phần', '[Danh sách người tham dự]'],
            ].map(([k, v], i) => `
            <table role="presentation" width="100%"><tr>
              <td width="96" style="padding:${i ? '8' : '0'}px 0 8px;color:#6b7280;font-size:13px;">${k}</td>
              <td style="padding:${i ? '8' : '0'}px 0 8px;color:#111827;font-size:14px;font-weight:600;">${v}</td>
            </tr></table>`).join('')}
          </td></tr>
        </table>
        <p style="margin:0 0 10px;color:#111827;font-size:14px;font-weight:600;">Nội dung dự kiến</p>
        <ul style="margin:0 0 24px;padding-left:20px;color:#374151;font-size:14px;line-height:1.8;">
          <li>[Mục 1]</li><li>[Mục 2]</li><li>[Mục 3]</li>
        </ul>
        <div>${btn('Tham gia họp')} &nbsp; <a href="#" style="display:inline-block;padding:12px 22px;border:1px solid #d1d5db;border-radius:8px;color:#374151;text-decoration:none;font-size:14px;font-weight:600;">Thêm vào lịch</a></div>`,
    }),
  },

  {
    id: 'reminder',
    labelKey: 'template.reminder',
    subject: 'Nhắc nhở',
    html: shell({
      accent: '#d97706',
      title: '⏰ Nhắc nhở',
      body: `
        ${p('Xin chào,')}
        ${p('Đây là email nhắc nhở thân thiện về việc dưới đây. Vui lòng hoàn tất trước thời hạn để tránh gián đoạn.')}
        <table role="presentation" width="100%" style="margin:4px 0 22px;">
          <tr><td style="padding:16px 18px;background:#fffbeb;border:1px solid #fde68a;border-radius:10px;">
            <p style="margin:0 0 4px;color:#92400e;font-size:13px;font-weight:600;text-transform:uppercase;letter-spacing:.3px;">Hạn chót</p>
            <p style="margin:0;color:#111827;font-size:18px;font-weight:700;">[dd/mm/yyyy · HH:mm]</p>
            <p style="margin:6px 0 0;color:#374151;font-size:14px;line-height:1.6;">[Nội dung cần hoàn tất]</p>
          </td></tr>
        </table>
        ${btn('Xử lý ngay', '#d97706')}`,
    }),
  },

  {
    id: 'thanks',
    labelKey: 'template.thankyou',
    subject: 'Cảm ơn bạn',
    html: shell({
      accent: '#059669',
      title: '🙏 Lời cảm ơn',
      body: `
        <p style="margin:0 0 14px;color:#111827;font-size:20px;font-weight:700;">Cảm ơn bạn, [Tên]!</p>
        ${p('Chúng tôi thực sự trân trọng thời gian và sự tin tưởng bạn dành cho chúng tôi. Sự hợp tác của bạn có ý nghĩa rất lớn.')}
        ${p('Nếu có bất kỳ câu hỏi hay cần hỗ trợ thêm, bạn chỉ cần phản hồi email này — chúng tôi luôn sẵn lòng.')}
        <table role="presentation" width="100%" style="margin:6px 0 22px;">
          <tr><td style="padding:16px 18px;background:#ecfdf5;border:1px solid #a7f3d0;border-radius:10px;">
            <p style="margin:0 0 2px;color:#065f46;font-size:13px;font-weight:600;">Bước tiếp theo</p>
            <p style="margin:0;color:#374151;font-size:14px;line-height:1.6;">[Gợi ý hành động tiếp theo, ví dụ: xem lại tài liệu, đặt lịch buổi kế…]</p>
          </td></tr>
        </table>
        ${btn('Tiếp tục', '#059669')}`,
    }),
  },

  {
    id: 'newsletter',
    labelKey: 'template.newsletter',
    subject: 'Bản tin cập nhật',
    html: shell({
      title: '📰 Bản tin',
      body: `
        <p style="margin:0 0 4px;color:#111827;font-size:18px;font-weight:700;">Những cập nhật nổi bật</p>
        <p style="margin:0 0 22px;color:#6b7280;font-size:13px;">Kỳ [tháng/năm]</p>
        ${[
          ['Tiêu đề mục 1', 'Tóm tắt ngắn gọn nội dung mục 1, đủ để người đọc nắm ý chính và muốn tìm hiểu thêm.'],
          ['Tiêu đề mục 2', 'Tóm tắt ngắn gọn nội dung mục 2. Giữ mỗi mục 1–2 câu để bản tin dễ đọc.'],
          ['Tiêu đề mục 3', 'Tóm tắt ngắn gọn nội dung mục 3 và dẫn tới liên kết chi tiết bên dưới.'],
        ].map(([t, d]) => `
        <div style="padding:16px 0;border-bottom:1px solid #f1f5f9;">
          <p style="margin:0 0 4px;color:#111827;font-size:16px;font-weight:600;">${t}</p>
          <p style="margin:0 0 8px;color:#374151;font-size:14px;line-height:1.7;">${d}</p>
          <a href="#" style="color:#2563eb;text-decoration:none;font-size:14px;font-weight:600;">Đọc thêm →</a>
        </div>`).join('')}
        <div style="margin-top:24px;">${btn('Xem tất cả')}</div>`,
    }),
  },
];
