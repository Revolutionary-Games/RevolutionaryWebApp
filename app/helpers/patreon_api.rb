# frozen_string_literal: true

require 'rest-client'

# Module with helper function to do patreon operations
module PatreonAPI
  def self.headers(patreon_token)
    { Authorization: "Bearer #{patreon_token}" }
  end

  def self.find_included_object(data, id)
    data['included'].each { |obj|
      return obj if obj['id'] == id
    }

    nil
  end

  def self.query_first_campaign_id(patreon_token)
    response = RestClient.get('https://www.patreon.com/api/oauth2/api/current_user/campaigns',
                              headers(patreon_token))

    data = JSON.parse(response.body)

    if data['data'].first['type'] != 'campaign'
      raise StandardError('invalid response object type')
    end

    data['data'].first['id']
  end

  def self.query_all_current_patrons(patreon_token, campaign_id)
    response = RestClient.get('https://www.patreon.com/api/oauth2/api/campaigns/' +
                              campaign_id.to_s + '/pledges?include=patron.null',
                              headers(patreon_token))

    data = JSON.parse(response.body)

    result = []

    logger = Rails.logger

    data['data'].each { |obj|
      next unless obj['type'] == 'pledge'

      patron_id = obj['relationships']['patron']['data']['id']

      user_data = PatreonAPI.find_included_object data, patron_id

      unless user_data
        logger.warning "could not find related object with id: #{patron_id}"
        next
      end
      result.push(pledge: obj, user: user_data)
    }

    # TODO: pagination!
    if data.include?('links') && data['links'].include?('next')
      logger.error 'patreon pagination is not done'
    end

    result
  end
end
