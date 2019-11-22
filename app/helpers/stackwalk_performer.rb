# frozen_string_literal: true

require 'rest-client'

# Module with helper function to call StackWalk web app on localhost
module StackwalkPerformer
  def self.perform_stackwalk(file, timeout: 60)
    logger = Rails.logger

    logger.debug "Calling stackwalk API with file #{file}"

    begin
      response = RestClient.post 'http://localhost:3211/api/v1',
                                 file: File.new(file, 'rb'), timeout: timeout

      return 'StackWalk service returned failure code', true if response.code != 200

      return response.body, false
    rescue StandardError => e
      return e.to_s, true
    end
  end
end
