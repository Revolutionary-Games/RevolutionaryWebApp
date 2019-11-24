# frozen_string_literal: true

# Sends emails about report status
class ReportStatusMailer < ActionMailer::Base
  default from: MailFromHelper.from

  def self.get_unsubscribe_token(email_address)
    payload = { unsubscribe: 'email',
                email: email_address,
                exp: (Time.zone.now + 30.days).to_i }

    JWT.encode payload, Rails.application.credentials[:secret_key_base], 'HS256'
  end

  def marked_duplicate(report_id)
    raise 'Base url not set' unless ENV['BASE_URL']

    @report = Report.find_by_id report_id

    return unless @report&.reporter_email

    @unsubscribe_token = ReportStatusMailer.get_unsubscribe_token @report.reporter_email

    mail(
      to: @report.reporter_email,
      subject: "[ThriveDevCenter] Your report ##{report_id} duplicate status changed"
    ) { |format|
      format.text
      format.html
    }
  end

  def solved_changed(report_id)
    raise 'Base url not set' unless ENV['BASE_URL']

    @report = Report.find_by_id report_id

    return unless @report&.reporter_email

    @unsubscribe_token = ReportStatusMailer.get_unsubscribe_token @report.reporter_email

    solve_text = @report.solved ? 'is now solved' : 'is no longer marked solved'

    mail(
      to: @report.reporter_email,
      subject: "[ThriveDevCenter] Your report ##{report_id} #{solve_text}"
    ) { |format|
      format.text
      format.html
    }
  end
end
