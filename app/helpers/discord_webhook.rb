require 'discordrb'
require 'discordrb/api'
require 'discordrb/webhooks'

module DiscordWebhook

  # TODO: would be nice to run this with sidekiq or something
  def self.sendCreatedReport(report)
    logger = Rails.logger
    if !ENV["DISCORD_REPORT_WEBHOOK"] || !ENV["DISCORD_RETURN_URL"]
      logger.debug "Discord webhook disabled due to missing env variables"
      return
    end

    webhookURL = ENV["DISCORD_REPORT_WEBHOOK"]

    match = webhookURL.match(/.*\/webhooks\/(\d+)\/.*$/i)

    # Extract what probably is the important parameter in it
    webhookMajorParameter = match.captures[0]
    
    # This is a hacky way to follow the rate limiting
    builder = Discordrb::Webhooks::Builder.new

    reportURL = URI::join(ENV["DISCORD_RETURN_URL"], "/report/#{report.id}").to_s
    
    builder.content = "New report created: #{reportURL}"

    # Send it
    # could append ?wait=true to the url
    response = Discordrb::API::request(:webhook, webhookMajorParameter, "post", webhookURL,
                                       builder.to_json_hash.to_json, content_type: :json)

    logger.debug "Got webhook response: #{response.code}"

    if response.code != 200 && response.code != 204
      logger.error "Failed to post to discord webhook"
    end
  end
end
