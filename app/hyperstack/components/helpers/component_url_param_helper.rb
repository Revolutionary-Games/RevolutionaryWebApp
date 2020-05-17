# frozen_string_literal: true

# Helper for components that store parameters in the URL to read and write them
module ComponentUrlParamHelper
  def set_values_from_query(config, query)
    set = false

    config.each { |key, value|
      next unless query.include? value[:param]

      parsed = nil

      begin
        parsed = if value[:type].is_a?(String)
                   query[value[:param]].send value[:type]
                 else
                   value[:type].call query[value[:param]]
                 end
      rescue StandardError => e
        puts "Invalid URL parameter: #{e}"
      end

      send "#{key}=", parsed
      set = true
    }

    set
  end

  # Build a query string from non-default values
  def build_query_from_values(config)
    query = ''
    first = true

    config.each { |key, value|
      obj_value = send key
      next if obj_value == value[:default]

      query += '&' unless first
      first = false
      # TODO: url encoding if needed
      query += "#{value[:param]}=#{obj_value}"
    }

    query
  end

  def self.parse_bool(value)
    value == 'true'
  end
end
