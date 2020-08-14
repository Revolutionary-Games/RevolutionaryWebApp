# frozen_string_literal: true

require 'rest-client'

COMMUNITY_FORUM_API_BASE ||= 'https://community.revolutionarygamesstudio.com/'
DEV_FORUM_API_BASE ||= 'https://forum.revolutionarygamesstudio.com/'

# Max number of patrons (TODO: pagination if needs more)
DISCOURSE_QUERY_LIMIT ||= 1000

# Module with helper function to do discourse user and group operations
module DiscourseApiHelper
  MAX_RETRIES_FOR_TOO_MANY_REQUESTS = 6

  def self.headers(type = :community)
    key = case type
          when :community
            ENV['COMMUNITY_DISCOURSE_API_KEY']
          when :dev
            ENV['DEV_DISCOURSE_API_KEY']
          else
            raise 'unknown forum type for headers'
          end

    { 'Api-Key' => key,
      'Api-Username' => 'system',
      content_type: :json }
  end

  def self.base_url(type = :community)
    case type
    when :community
      COMMUNITY_FORUM_API_BASE
    when :dev
      DEV_FORUM_API_BASE
    else
      raise 'unknown forum type for base_url'
    end
  end

  def self.perform_with_rate_limit
    (1..MAX_RETRIES_FOR_TOO_MANY_REQUESTS).each { |i|
      begin
        return yield
      rescue RestClient::TooManyRequests
        Rails.logger.info 'Too many requests sent to discourse, ' +
             (if i < MAX_RETRIES_FOR_TOO_MANY_REQUESTS
                "retry attempt #{i}"
              else
                'ran out of retries'
              end)
        sleep 1 + i * 4
      end
    }

    raise "Discourse API request ran out of retries"
  end

  def self.query_users_in_group(group)
    offset = 0
    limit = DISCOURSE_QUERY_LIMIT

    url = URI.join(COMMUNITY_FORUM_API_BASE,
                   "/groups/#{group}/members.json?offset=#{offset}&limit=#{limit}").to_s

    response = perform_with_rate_limit {
      RestClient::Request.execute(method: :get, url: url,
                                  timeout: 120,
                                  headers: DiscourseApiHelper.headers)
    }

    data = JSON.parse(response.body)

    [data['members'], data['owners']]
  end

  # TODO: find if there is some more efficient API for this
  def self.query_group_owners(group)
    _, owners = query_users_in_group group

    owners
  end

  # TODO: is this really, really slow?
  def self.find_user_by_email(email, type: :community)
    url = URI.join(base_url(type), 'admin/users/list/all.json').to_s + "?email=#{email}"
    begin
      response = perform_with_rate_limit {
        RestClient.get(url, headers(type))
      }
    rescue RestClient::NotFound
      return nil
    end

    JSON.parse(response.body).first
  end

  # Returns way more info than find user by email
  def self.user_info_by_name(username, type: :community)
    url = URI.join(base_url(type), "users/#{username}.json").to_s
    response = perform_with_rate_limit { RestClient.get(url, headers(type)) }

    JSON.parse(response.body)['user']
  end

  def self.get_group_id(group)
    url = URI.join(COMMUNITY_FORUM_API_BASE,
                   "/groups/#{group}.json").to_s
    response = perform_with_rate_limit { RestClient.get(url, DiscourseApiHelper.headers) }

    JSON.parse(response.body)['group']['id']
  end

  def self.prepapare_group_url_and_payload(group, usernames)
    id = get_group_id group

    url = URI.join(COMMUNITY_FORUM_API_BASE,
                   "/groups/#{id}/members.json").to_s

    payload = { usernames: usernames.join(',') }

    [url, payload]
  end

  def self.add_group_members(group, usernames)
    url, payload = prepapare_group_url_and_payload group, usernames

    perform_with_rate_limit {
      RestClient.put(url, payload.to_json, DiscourseApiHelper.headers)
    }
  end

  def self.remove_group_members(group, usernames)
    url, payload = prepapare_group_url_and_payload group, usernames

    perform_with_rate_limit {
      RestClient::Request.execute(method: :delete, url: url,
                                  payload: payload.to_json,
                                  headers: DiscourseApiHelper.headers)
    }
  end
end
