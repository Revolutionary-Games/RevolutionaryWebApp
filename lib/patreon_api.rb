# frozen_string_literal: true

require 'rest-client'

# Module with helper function to do patreon operations
module PatreonAPI
  def self.headers(patreon_token)
    { Authorization: "Bearer #{patreon_token}" }
  end

  def self.find_included_object(data, id, type = nil)
    data['included'].each { |obj|
      next unless obj['id'] == id
      next if !type.nil? && obj['type'] != type

      return obj
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
          campaign_id.to_s + '/pledges?include=patron.null,reward&fields%5Bpledge%5D=status,currency'

    while url

      response = RestClient.get(url, headers(patreon_token))

      data = JSON.parse(response.body)

      data['data'].each { |obj|
        next unless obj['type'] == 'pledge'

        patron_id = obj['relationships']['patron']['data']['id']
        patron_obj_type = obj['relationships']['patron']['data']['type']

        user_data = PatreonAPI.find_included_object data, patron_id, patron_obj_type

        unless user_data
          logger.warn "could not find related object with id: #{patron_id}"
          next
        end

        unless obj['relationships'].include? 'reward'
          logger.error 'reward data is not included for user'
          next
        end

        reward_id = obj['relationships']['reward']['data']['id']
        reward_type = obj['relationships']['reward']['data']['type']

        reward_data = PatreonAPI.find_included_object data, reward_id, reward_type

        unless reward_data
          logger.warn "could not find reward object with id: #{patron_id}"
          next
        end

        result.push(pledge: obj, user: user_data, reward: reward_data)
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
    fields_user = CGI.escape('fields[user]') + '=email,full_name,vanity,url'
    fields_member = CGI.escape('fields[member]') + '=patron_status,email,' \
                                                   'currently_entitled_amount_cents'

    fields = fields_user + '&' + fields_member

    response = RestClient.get('https://www.patreon.com/api/oauth2/v2/identity?' \
                              "include=memberships&#{fields}", headers(access_token))

    JSON.parse response.body
  end

  def self.get_membership(data, id)
    data['included']&.each { |obj|
      return obj if obj['id'] == id
    }

    nil
  end

  # This is untested / unworking
  def self.get_user_memberships_over_cents(user_info, cents)
    return [] unless user_info['data']['memberships']['data']

    result = []

    user_info['data']['memberships']['data'].each { |membership|
      actual_data = get_membership user_info, membership['id']

      if actual_data && actual_data['attributes']['currently_entitled_amount_cents'] >= cents
        result.append membership['id']
      end
    }

    result
  end

  def self.get_user_memberships(access_token, member_ids)
    fields_campaign = CGI.escape('fields[campaign]') + '=vanity,creation_name,one_liner,url'
    fields_tier = CGI.escape('fields[tier]') + '=title,amount_cents'

    fields = fields_campaign + '&' + fields_tier

    results = []

    member_ids.each { |id|
      response = RestClient.get("https://www.patreon.com/api/oauth2/v2/members/#{id}?" \
                                "include=currently_entitled_tiers,campaign&#{fields}",
                                headers(access_token))

      results.append(JSON.parse(response.body))
    }

    results
  end
end
