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
    logger = Rails.logger

    result = []

    url = 'https://www.patreon.com/api/oauth2/api/campaigns/' +
          campaign_id.to_s + '/pledges?include=patron.null'

    while url

      response = RestClient.get(url, headers(patreon_token))

      data = JSON.parse(response.body)

      data['data'].each { |obj|
        next unless obj['type'] == 'pledge'

        patron_id = obj['relationships']['patron']['data']['id']

        user_data = PatreonAPI.find_included_object data, patron_id

        unless user_data
          logger.warn "could not find related object with id: #{patron_id}"
          next
        end
        result.push(pledge: obj, user: user_data)
      }

      # Pagination
      url = if data.include?('links') && data['links'].include?('next')
              data['links']['next']
            else
              # No more pages time to break the loop
              nil
            end
    end

    result
  end

  def self.turn_code_into_tokens(code, client_id, client_secret, redirect_uri)
    response = RestClient.post('https://www.patreon.com/api/oauth2/token',
                               code: code,
                               grant_type: 'authorization_code',
                               client_id: client_id,
                               client_secret: client_secret,
                               redirect_uri: redirect_uri)

    data = JSON.parse response.body

    access = data['access_token']
    refresh = data['refresh_token']
    expires = data['expires_in']

    if data['token_type'] != 'Bearer' || access.blank? || refresh.blank? || expires.blank?
      raise "Invalid patreon json response: #{data}"
    end

    [access, refresh, expires]
  end

  def self.get_logged_in_user_details(access_token)
    response = RestClient.get('https://www.patreon.com/api/oauth2/v2/identity?' \
                              'include=memberships&fields[user]=about,email,'\
                              'full_name,vanity', headers(access_token))

    JSON.parse response.body
  end
end
