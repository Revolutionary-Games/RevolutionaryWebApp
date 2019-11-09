# frozen_string_literal: true

# Removes the reporter email from reports if they want to unsubscribe
class ProcessEmailUnsubscribeRequest < Hyperstack::ServerOp
  param acting_user: nil, nils: true
  param :key, type: String

  step {
    data = nil
    begin
      decoded_token = JWT.decode params.key, Rails.application.credentials[:secret_key_base],
                                 true, algorithm: 'HS256'
      data = decoded_token[0]
    rescue JWT::DecodeError => e
      raise "Invalid token: #{e}"
    end

    raise 'Invalid token: not of email type' if data['unsubscribe'] != 'email'

    Report.where(reporter_email: data['email']).each { |report|
      report.reporter_email = nil
      report.save
    }
  }
end
